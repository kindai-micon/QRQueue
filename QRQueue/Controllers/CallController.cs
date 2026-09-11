using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using QRQueue.Hubs;
using QRQueue.Models;
using QRQueue.Models.API;
using QRQueue.Services;
using System.Net.NetworkInformation;

namespace QRQueue.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CallController/*(ApplicationDbContext applicationDbContext)*/ : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IQueueCallService _queueCallService;
        private readonly IPushSubscriptionService _pushSubscriptionService;  //再呼び出し(Again)用.
        private readonly ILineService _lineService;  //再呼び出し(Again)のLINE通知用.
        private readonly IHubContext<QueueHub> _hubContext;  // 受付状態の即時配信(issue #65)用.
        private readonly ICheckinCodeService _checkinCodeService;
        private readonly IQrCodeGenerator _qrCodeGenerator;
        private readonly IBaseUrlResolver _baseUrlResolver;

        public CallController(
                 ApplicationDbContext db,
                 IQueueCallService queueCallService,
                 IPushSubscriptionService pushSubscriptionService,
                 ILineService lineService,
                 IHubContext<QueueHub> hubContext,
                 ICheckinCodeService checkinCodeService,
                 IQrCodeGenerator qrCodeGenerator,
                 IBaseUrlResolver baseUrlResolver)
        {
            _db = db;
            _queueCallService = queueCallService;
            _pushSubscriptionService = pushSubscriptionService;
            _lineService = lineService;
            _hubContext = hubContext;
            _checkinCodeService = checkinCodeService;
            _qrCodeGenerator = qrCodeGenerator;
            _baseUrlResolver = baseUrlResolver;
        }

        /// <summary>
        /// 受付確認QRのPNG(issue #76)。
        /// 30秒で回転する到着確認コードを含むURLのQRを返す。
        /// 受付画面(/checkin-qr/{eventDisplayId})がこの間隔で再読み込みして表示する。
        /// 撮影・共有されたQRはコード失効後に使用できない。
        /// 固定版は別途 掲示PDF(GET /api/pdf/checkin/{eventDisplayId})で発行する。
        /// </summary>
        [Authorize(Policy = "CallView")]
        [HttpGet("checkin-qrcode/{eventDisplayId}")]
        public async Task<IActionResult> CheckinQrCode(Guid eventDisplayId)
        {
            var ev = await _db.Events.FirstOrDefaultAsync(x => x.DisplayId == eventDisplayId);
            if (ev == null)
            {
                return NotFound();
            }
            var code = await _checkinCodeService.GetCurrentCheckinCodeAsync(eventDisplayId);
            var url = $"{_baseUrlResolver.Resolve(Request)}/checkin/{eventDisplayId}?rc={code}";
            return File(_qrCodeGenerator.GeneratePng(url, 400, 400), "image/png");
        }

        /// <summary>
        /// 参加登録QRのPNG。Web掲示画面(/entry-qr/{eventDisplayId})用。
        /// チェックインQRと同じ仕様: 画面表示は30秒で回転する到着確認コードを含むURLのQRを返す。
        /// 掲示画面がこの間隔で再読み込みして表示する。固定版は別途 掲示PDF(GET /api/pdf/entry/{eventDisplayId})で発行する。
        /// </summary>
        [Authorize(Policy = "CallView")]
        [HttpGet("entry-qrcode/{eventDisplayId}")]
        public async Task<IActionResult> EntryQrCode(Guid eventDisplayId)
        {
            var ev = await _db.Events.FirstOrDefaultAsync(x => x.DisplayId == eventDisplayId);
            if (ev == null)
            {
                return NotFound();
            }
            var code = await _checkinCodeService.GetCurrentCheckinCodeAsync(eventDisplayId);
            var url = $"{_baseUrlResolver.Resolve(Request)}/entry/{eventDisplayId}?rc={code}";
            return File(_qrCodeGenerator.GeneratePng(url, 400, 400), "image/png");
        }

        [Authorize(Policy = "EventOpenClose")]
        [HttpPut("open/{eventDisplayId}")]
        public async Task<IActionResult> Open(Guid eventDisplayId)
        {
            var ev = await _db.Events.FirstOrDefaultAsync(x => x.DisplayId == eventDisplayId);
            if (ev == null)
            {
                return NotFound();
            }
            ev.Status = EventStatus.Open;
            await _db.SaveChangesAsync();
            // 受付状態の変更を参加者画面へ即時配信(issue #65)
            await NotifyStatusChangedAsync(ev.DisplayId);
            return Ok();
        }

        [Authorize(Policy = "EventOpenClose")]
        [HttpPut("close/{eventDisplayId}")]
        public async Task<IActionResult> Close(Guid eventDisplayId)
        {
            var ev = await _db.Events.FirstOrDefaultAsync(x => x.DisplayId == eventDisplayId);
            if (ev == null)
            {
                return NotFound();
            }
            ev.Status = EventStatus.Closed;
            await _db.SaveChangesAsync();
            // 受付状態の変更を参加者画面へ即時配信(issue #65)
            await NotifyStatusChangedAsync(ev.DisplayId);
            return Ok();
        }

        /// <summary>参加登録画面などに受付状態の変化を通知する(issue #65)</summary>
        private Task NotifyStatusChangedAsync(Guid eventDisplayId)
        {
            return _hubContext.Clients.Group(eventDisplayId.ToString()).SendAsync("UpdateStatus");
        }

        /// <summary>自動呼出し(AutoNext)のオン/オフ切替。オフにすると次の呼び出しはスタッフの「次を呼ぶ」のみで行う</summary>
        public record AutoNextRequest(bool Enabled);

        [Authorize(Policy = "CallExecute")]
        [HttpPut("autonext/{eventDisplayId}")]
        public async Task<IActionResult> SetAutoNext(Guid eventDisplayId, [FromBody] AutoNextRequest request)
        {
            var ev = await _db.Events.FirstOrDefaultAsync(x => x.DisplayId == eventDisplayId);
            if (ev == null)
            {
                return NotFound();
            }

            ev.AutoNextEnabled = request.Enabled;
            await _db.SaveChangesAsync();
            // 呼び出しコンソールへ即時反映
            await NotifyStatusChangedAsync(eventDisplayId);
            return Ok(new { autoNextEnabled = ev.AutoNextEnabled });
        }

        [Authorize(Policy = "CallExecute")]
        [HttpPut("next/{eventDisplayId}")]
        public async Task<IActionResult> Next(Guid eventDisplayId)
        {
            // 「次を呼ぶ」は QueueCallService に一本化。
            // 呼出中の未チェックイングループの割込pool退避・方式②プール自動確定・通知もここで行う。
            var ev = await _db.Events.FirstOrDefaultAsync(x => x.DisplayId == eventDisplayId);
            if (ev == null)
            {
                return NotFound();
            }

            var called = await _queueCallService.CallNextAsync(ev);
            if (called == null)
            {
                return NoContent();
            }

            return Ok();
        }

        [Authorize(Policy = "CallExecute")]
        [HttpPut("again/{eventDisplayId}")]
        public async Task<IActionResult> Again(Guid eventDisplayId)
        {
            var ev = await _db.Events.FirstOrDefaultAsync(x => x.DisplayId == eventDisplayId);
            if (ev == null)
            {
                return NotFound();
            }

            // 再呼び出しもイベント排他制御下で直列化し、状態更新の競合を防ぐ(issue #82)
            var callingGroup = await _queueCallService.RunExclusiveAsync(ev, async () =>
            {
                var group = await _db.ParticipationGroups
                    .Include(x => x.Tickets)
                    .FirstOrDefaultAsync(x =>
                        x.Event.DisplayId == eventDisplayId &&
                        x.Status == GroupStatus.Calling);
                if (group == null)
                {
                    return null;
                }
                if (group.Tickets.Count == 0)    //チケットがなかった時を想定.
                {
                    return null;
                }
                await _pushSubscriptionService.SendNotifyTicketGroupAsync(group.Tickets.ToList(), "再度呼び出し", "再度呼び出しが行われました。");
                // LINE連携済みのチケットにも Messaging API で通知する(初回呼び出しと併用)
                await _lineService.SendNotifyAsync(
                    group.Tickets.Select(t => t.DisplayId).ToList(),
                    $"{ev.Name}でもう一度呼び出しが行われました。ブースまでお越しください");
                // 電子券画面の即時表示用に SignalR でも配信(初回呼び出しと同様)
                await _hubContext.Clients.Group(ev.DisplayId.ToString()).SendAsync("Called", new
                {
                    groupNumber = group.Number,
                    groupDisplayId = group.DisplayId.ToString()
                });
                group.CallCount++;
                group.CalledAt = DateTimeOffset.UtcNow;
                await _db.SaveChangesAsync();
                return group;
            });

            if (callingGroup == null)
            {
                return NotFound();
            }
            return Ok();
        }

        /// <summary>
        /// グループを優先待機(Interrupted)へ移動する(issue #73)。
        /// 未到着グループを枠から除外して到着済みグループだけでゲームを進めたい場合や、
        /// 到着済みだが次の枠へ回したい場合にスタッフが使用する。
        /// </summary>
        [Authorize(Policy = "CallExecute")]
        [HttpPut("group/{groupDisplayId}/interrupt")]
        public async Task<IActionResult> InterruptGroup(Guid groupDisplayId)
        {
            var group = await _db.ParticipationGroups
                .Include(x => x.Event)
                .Include(x => x.Tickets)
                .FirstOrDefaultAsync(x => x.DisplayId == groupDisplayId);
            if (group == null)
            {
                return NotFound();
            }
            if (group.Status is not (GroupStatus.Calling or GroupStatus.Completed))
            {
                return Conflict("呼び出し中またはチェックイン済みのグループのみ優先待機へ移動できます");
            }

            group.Status = GroupStatus.Interrupted;
            await _db.SaveChangesAsync();

            await _hubContext.Clients.Group(group.Event.DisplayId.ToString()).SendAsync("QueueChanged");
            return Ok(new { groupNumber = group.Number, status = group.Status.ToString() });
        }

        /// <summary>
        /// グループの棄権処理(チケット無効化)(issue #73)。
        /// 未到着グループを棄権扱いにし、グループと有効チケットをすべて無効化する。
        /// 無効化されたチケットは再利用できない。スタッフのみ実行できる(CallExecute)。
        /// </summary>
        [Authorize(Policy = "CallExecute")]
        [HttpPut("group/{groupDisplayId}/forfeit")]
        public async Task<IActionResult> ForfeitGroup(Guid groupDisplayId)
        {
            var group = await _db.ParticipationGroups
                .Include(x => x.Event)
                .Include(x => x.Tickets)
                .FirstOrDefaultAsync(x => x.DisplayId == groupDisplayId);
            if (group == null)
            {
                return NotFound();
            }
            if (group.Status is GroupStatus.Cancelled)
            {
                return Conflict("既に取り消されています");
            }

            group.Status = GroupStatus.Cancelled;
            group.JoinToken = null;
            var cancelled = 0;
            foreach (var ticket in group.Tickets.Where(t => t.Status == TicketStatus.Registered))
            {
                ticket.Status = TicketStatus.Cancelled;
                cancelled++;
            }
            await _db.SaveChangesAsync();

            await _hubContext.Clients.Group(group.Event.DisplayId.ToString()).SendAsync("QueueChanged");
            return Ok(new { groupNumber = group.Number, cancelledTickets = cancelled });
        }

        /// <summary>
        /// 優先待機(Interrupted/割り込みプール)のグループを直接呼び出す。
        /// 代表者のチェックインを待たずに、スタッフの判断で優先プールのグループを呼び出したい場合に使用する。
        /// 状態を Calling へ移し、SignalR/Web Push/LINE の告知も行う(QueueCallService.CallInterruptedGroupAsync)。
        /// </summary>
        [Authorize(Policy = "CallExecute")]
        [HttpPut("group/{groupDisplayId}/call")]
        public async Task<IActionResult> CallInterruptedGroup(Guid groupDisplayId)
        {
            var group = await _db.ParticipationGroups
                .Include(x => x.Event)
                .FirstOrDefaultAsync(x => x.DisplayId == groupDisplayId);
            if (group == null)
            {
                return NotFound();
            }

            var called = await _queueCallService.CallInterruptedGroupAsync(group.Event, groupDisplayId);
            if (called == null)
            {
                return Conflict("優先待機(割り込みプール)のグループのみ直接呼び出せます");
            }
            return Ok(new { groupNumber = group.Number, status = GroupStatus.Calling.ToString() });
        }

        [Authorize(Policy = "CallView")]
        [HttpGet("queue/{eventDisplayId}")]
        public async Task<ActionResult<QueueView>> Queue(Guid eventDisplayId)
        {
            var view = new QueueView();

            var ev = await _db.Events.FirstOrDefaultAsync(x => x.DisplayId == eventDisplayId);
            if (ev == null)
            {
                return NotFound();
            }

            // 一定時間を超えた未到着グループを優先待機へ退避(issue #69)
            await _queueCallService.EvacuateExpiredSlotGroupsAsync(ev);

            // ステータス別のグループ取得を1クエリに統合(ポーリング対象のホットパスのため)。
            // Waiting/CalledAt順はメモリ側で並べ替えて従来と同じ順序を維持する。
            var activeGroups = await _db.ParticipationGroups
                .Include(x => x.Tickets)
                .Where(x => x.Event.DisplayId == eventDisplayId &&
                    (x.Status == GroupStatus.Waiting ||
                     x.Status == GroupStatus.Calling ||
                     x.Status == GroupStatus.Interrupted ||
                     x.Status == GroupStatus.Matching))
                .ToListAsync();

            var waitingGroups = activeGroups
                .Where(x => x.Status == GroupStatus.Waiting)
                .OrderBy(x => x.Number)
                .ToList();
            var callingGroups = activeGroups
                .Where(x => x.Status == GroupStatus.Calling)
                .OrderBy(x => x.CalledAt)
                .ToList();
            var interruptedGroups = activeGroups
                .Where(x => x.Status == GroupStatus.Interrupted)
                .OrderBy(x => x.Number)
                .ToList();
            var matchingGroups = activeGroups
                .Where(x => x.Status == GroupStatus.Matching)
                .ToList();

            view.WaitingGroup = waitingGroups.Select(x => new ParticipationGroupView()
            {
                Number = x.Number,
                People = x.Tickets.Count(t => t.Status != TicketStatus.Cancelled),
                Status = x.Status,
                DisplayId = x.DisplayId
            });

            view.CallingGroup = callingGroups.Select(x => new ParticipationGroupView()
            {
                Number = x.Number,
                People = x.Tickets.Count(t => t.Status != TicketStatus.Cancelled),
                Status = x.Status,
                DisplayId = x.DisplayId
            });

            view.InterruptedGroup = interruptedGroups.Select(x => new ParticipationGroupView()
            {
                Number = x.Number,
                People = x.Tickets.Count(t => t.Status != TicketStatus.Cancelled),
                Status = x.Status,
                DisplayId = x.DisplayId
            });

            view.PeoplePool = matchingGroups.Sum(x => x.Tickets.Count(t => t.Status != TicketStatus.Cancelled));

            // ゲーム参加枠ごとの到着状況(issue #69):
            // Calling/Interrupted のグループが属する枠(=まだ処理中の枠)を対象に、
            // 枠内の各グループの到着(チェックイン)状態を返す
            var activeSlotIds = callingGroups.Concat(interruptedGroups)
                .Where(x => x.GameSlotId != null)
                .Select(x => x.GameSlotId!.Value)
                .Distinct()
                .ToList();

            if (activeSlotIds.Count > 0)
            {
                var slotGroups = await _db.ParticipationGroups
                    .Include(x => x.Tickets)
                    .Where(x => x.Event.DisplayId == eventDisplayId && activeSlotIds.Contains(x.GameSlotId!.Value))
                    .ToListAsync();

                view.Slots = slotGroups
                    .GroupBy(x => x.GameSlotId!.Value)
                    .Select(g => new GameSlotView
                    {
                        SlotId = g.Key.ToString(),
                        CalledAt = g.Max(x => x.CalledAt),
                        AllArrived = g.All(x => x.Status == GroupStatus.Completed),
                        Groups = g.OrderBy(x => x.Number).Select(x => new ParticipationGroupView
                        {
                            Number = x.Number,
                            People = x.Tickets.Count(t => t.Status != TicketStatus.Cancelled),
                            Status = x.Status,
                            DisplayId = x.DisplayId
                        })
                    })
                    .OrderByDescending(x => x.CalledAt)
                    .ToList();
            }

            return view;
        }
    }
}
