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
        private readonly IHubContext<QueueHub> _hubContext;  // 受付状態の即時配信(issue #65)用.

        public CallController(
                 ApplicationDbContext db,
                 IQueueCallService queueCallService,
                 IPushSubscriptionService pushSubscriptionService,
                 IHubContext<QueueHub> hubContext)
        {
            _db = db;
            _queueCallService = queueCallService;
            _pushSubscriptionService = pushSubscriptionService;
            _hubContext = hubContext;
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
        /// ゲーム終了の確定(issue #71)。
        /// チェックイン済み(Completed)グループの有効チケットを「使用済み(Used)」にする。
        /// groupNumber 未指定の場合は、直近に呼び出された枠(CalledAt が最大の Completed
        /// グループ群=同時呼び出しされたグループ全体)を対象とする。
        /// ※ Updated はレコード作成時刻のみを反映するため直近特定には使えない(レビュー指摘)。
        /// 使用済みチケットは再チェックイン・再呼び出しの対象にならず、
        /// 同じ参加者は新しいチケットで再度受付できる。
        /// </summary>
        [Authorize(Policy = "CallExecute")]
        [HttpPut("done/{eventDisplayId}")]
        public async Task<IActionResult> Done(Guid eventDisplayId, [FromBody] long? groupNumber)
        {
            var query = _db.ParticipationGroups
                .Include(g => g.Tickets)
                .Where(g => g.Event.DisplayId == eventDisplayId);

            List<ParticipationGroup> targets;
            if (groupNumber.HasValue)
            {
                var group = await query.FirstOrDefaultAsync(g => g.Number == groupNumber.Value);
                if (group == null)
                {
                    return NotFound();
                }
                targets = new List<ParticipationGroup> { group };
            }
            else
            {
                // 直近に呼び出された枠(同時呼び出しされたグループ群)をまとめて対象にする
                var latestCalledAt = await query
                    .Where(g => g.Status == GroupStatus.Completed && g.CalledAt != null)
                    .MaxAsync(g => (DateTimeOffset?)g.CalledAt);
                if (latestCalledAt == null)
                {
                    return NotFound();
                }
                targets = await query
                    .Where(g => g.Status == GroupStatus.Completed && g.CalledAt == latestCalledAt)
                    .ToListAsync();
            }

            if (targets.Any(g => g.Status != GroupStatus.Completed))
            {
                return Conflict("チェックイン済みのグループのみ使用済みにできます");
            }

            var usedTickets = 0;
            foreach (var group in targets)
            {
                foreach (var ticket in group.Tickets.Where(t => t.Status == TicketStatus.Registered))
                {
                    ticket.Status = TicketStatus.Used;
                    usedTickets++;
                }
            }
            await _db.SaveChangesAsync();

            var ev = await _db.Events.FirstAsync(x => x.DisplayId == eventDisplayId);
            await _hubContext.Clients.Group(eventDisplayId.ToString()).SendAsync("QueueChanged");

            return Ok(new
            {
                groupNumber = targets.First().Number,
                groupNumbers = targets.Select(g => g.Number),
                usedTickets = usedTickets,
                eventName = ev.Name
            });
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

            var waitingGroups = await _db.ParticipationGroups.Include(x => x.Tickets).Where(x => x.Event.DisplayId == eventDisplayId && x.Status == GroupStatus.Waiting).OrderBy(x => x.Number).ToListAsync();

            var callingGroups = await _db.ParticipationGroups.Include(x => x.Tickets).Where(x => x.Event.DisplayId == eventDisplayId && x.Status == GroupStatus.Calling).OrderBy(x => x.CalledAt).ToListAsync();

            var interruptedGroups = await _db.ParticipationGroups.Include(x => x.Tickets).Where(x => x.Event.DisplayId == eventDisplayId && x.Status == GroupStatus.Interrupted).OrderBy(x => x.Number).ToListAsync();

            var matchingGroups = await _db.ParticipationGroups.Include(x => x.Tickets).Where(x => x.Event.DisplayId == eventDisplayId && x.Status == GroupStatus.Matching).ToListAsync();

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
