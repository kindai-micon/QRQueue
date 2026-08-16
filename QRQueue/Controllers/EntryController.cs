using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using QRQueue.Hubs;
using QRQueue.Models;
using QRQueue.Repositories;
using QRQueue.Services;

namespace QRQueue.Controllers
{
    /// <summary>
    /// 参加者向けAPI(匿名・認証なし、設計§6.1)。
    /// 参加登録・電子券の復元・チェックイン・グループ参加。
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    public class EntryController(
        IEventRepository eventRepository,
        IParticipationGroupRepository groupRepository,
        ITicketRepository ticketRepository,
        IGroupNumberIssuanceService groupNumberIssuanceService,
        IQueueCallService queueCallService,
        IQrCodeGenerator qrCodeGenerator,
        IHubContext<QueueHub> hubContext,
        IConfiguration configuration) : ControllerBase
    {
        public record JoinRequest(Guid EventDisplayId, string Mode, Guid ParticipantToken, bool Overwrite);
        public record RestoreRequest(Guid EventDisplayId, Guid ParticipantToken);
        public record GroupJoinRequest(string JoinToken, Guid ParticipantToken);

        /// <summary>参加登録画面の初期化(イベント名・受付状態・グループ上限)</summary>
        [HttpGet("{eventDisplayId}")]
        public async Task<IActionResult> GetEventInfo(Guid eventDisplayId)
        {
            var ev = await eventRepository.FindByDisplayIdAsync(eventDisplayId);
            if (ev == null)
            {
                return NotFound("イベントが見つかりません");
            }
            return Ok(new
            {
                eventName = ev.Name,
                status = ev.Status.ToString(),
                isOpen = ev.Status == EventStatus.Open,
                maxGroupSize = 3
            });
        }

        /// <summary>
        /// 参加登録(設計§4.1〜§4.3)。mode: solo=即キュー+採番 / pool=マッチングプールへ /
        /// group-create=グループ作成+代表者登録+採番。
        /// participantToken が既存の有効な参加に一致する場合は 409
        /// (クライアントは overwrite フラグで上書き=§4.4、または既存券を復元)。
        /// </summary>
        [HttpPost("join")]
        public async Task<IActionResult> Join([FromBody] JoinRequest request)
        {
            var ev = await eventRepository.FindByDisplayIdAsync(request.EventDisplayId);
            if (ev == null)
            {
                return NotFound("イベントが見つかりません");
            }
            if (ev.Status != EventStatus.Open)
            {
                return Conflict("受付中ではありません");
            }

            var existing = await ticketRepository.FindActiveByParticipantTokenAsync(
                request.ParticipantToken, ev.DisplayId);
            if (existing != null)
            {
                if (!request.Overwrite)
                {
                    return Conflict(new
                    {
                        message = "既に参加登録済みです",
                        ticketDisplayId = existing.DisplayId.ToString()
                    });
                }
                var leaveError = await LeaveCurrentGroupAsync(existing);
                if (leaveError != null)
                {
                    return Conflict(leaveError);
                }
            }

            // 上書き時はチケットの付け替え(§4.4: DisplayId が変わらないため Push 購読も引き継がれる)
            var isNewTicket = existing == null;
            var ticket = existing ?? new Ticket { ParticipantToken = request.ParticipantToken };

            switch (request.Mode)
            {
                case "solo":
                {
                    var group = new ParticipationGroup
                    {
                        EventId = ev.Id,
                        Type = GroupType.Solo,
                        Status = GroupStatus.Waiting
                    };
                    await groupRepository.AddAsync(group);
                    ticket.ParticipationGroupId = group.Id;
                    if (isNewTicket)
                    {
                        await ticketRepository.AddAsync(ticket);
                    }
                    // 採番(Serializable トランザクション内でグループ・チケットも一緒に保存される §4.5)
                    await groupNumberIssuanceService.IssueNumberAsync(group);
                    await NotifyJoinedAsync(ev);
                    return Ok(new { ticketDisplayId = ticket.DisplayId.ToString(), groupNumber = group.Number });
                }
                case "pool":
                {
                    var group = new ParticipationGroup
                    {
                        EventId = ev.Id,
                        Type = GroupType.AutoMatched,
                        Status = GroupStatus.Matching
                    };
                    await groupRepository.AddAsync(group);
                    ticket.ParticipationGroupId = group.Id;
                    if (isNewTicket)
                    {
                        await ticketRepository.AddAsync(ticket);
                    }
                    await ticketRepository.SaveChangesAsync();

                    // 満員成立: プールが設定人数に達したら即座にグループを成立させる(§4.2)
                    var pool = await groupRepository.GetMatchingPoolAsync(ev.Id);
                    ParticipationGroup? formed = null;
                    if (pool.Count >= ev.AutoGroupSize)
                    {
                        formed = await queueCallService.FormGroupFromMatchingPoolAsync(ev, ev.AutoGroupSize);
                    }
                    await NotifyJoinedAsync(ev);

                    return Ok(new
                    {
                        ticketDisplayId = ticket.DisplayId.ToString(),
                        groupNumber = formed != null && ticket.ParticipationGroupId == formed.Id
                            ? formed.Number
                            : (long?)null
                    });
                }
                case "group-create":
                {
                    var joinToken = Guid.CreateVersion7().ToString("N");
                    var group = new ParticipationGroup
                    {
                        EventId = ev.Id,
                        Type = GroupType.Manual,
                        Status = GroupStatus.Waiting,
                        JoinToken = joinToken
                    };
                    await groupRepository.AddAsync(group);
                    ticket.ParticipationGroupId = group.Id;
                    if (isNewTicket)
                    {
                        await ticketRepository.AddAsync(ticket);
                    }
                    // 代表者登録時点で採番(メンバーが揃うのを待たない §4.3)
                    await groupNumberIssuanceService.IssueNumberAsync(group);
                    await NotifyJoinedAsync(ev);
                    return Ok(new
                    {
                        ticketDisplayId = ticket.DisplayId.ToString(),
                        groupNumber = group.Number,
                        joinToken
                    });
                }
                default:
                    return BadRequest("mode は solo / pool / group-create のいずれかを指定してください");
            }
        }

        /// <summary>同一端末から電子券を復元(localStorage の participantToken → URL喪失対策 §6.1)</summary>
        [HttpPost("restore")]
        public async Task<IActionResult> Restore([FromBody] RestoreRequest request)
        {
            var ev = await eventRepository.FindByDisplayIdAsync(request.EventDisplayId);
            if (ev == null)
            {
                return NotFound("イベントが見つかりません");
            }

            var ticket = await ticketRepository.FindActiveByParticipantTokenAsync(
                request.ParticipantToken, ev.DisplayId);
            if (ticket == null)
            {
                return NotFound("参加登録が見つかりません");
            }
            return Ok(new { ticketDisplayId = ticket.DisplayId.ToString() });
        }

        /// <summary>
        /// チェックイン(受付の確定、設計§4.6)。participantToken で特定した参加者の属するグループが
        /// Calling なら Completed に確定して AutoNext を発火。Interrupted なら同様に完了し、
        /// 次の呼び出しに割り込んで処理対象にする。Waiting/Matching なら 409。
        /// 代表者でない場合も 409。
        /// </summary>
        [HttpPost("checkin")]
        public async Task<IActionResult> Checkin([FromBody] RestoreRequest request)
        {
            var ev = await eventRepository.FindByDisplayIdAsync(request.EventDisplayId);
            if (ev == null)
            {
                return NotFound("イベントが見つかりません");
            }

            var ticket = await ticketRepository.FindActiveByParticipantTokenAsync(
                request.ParticipantToken, ev.DisplayId);
            if (ticket == null || ticket.ParticipationGroupId == null)
            {
                return NotFound("このイベントでの参加登録が見つかりません");
            }

            var group = await groupRepository.FindByIdAsync(ticket.ParticipationGroupId.Value);
            if (group == null || group.Status == GroupStatus.Cancelled)
            {
                return Conflict("この参加はキャンセルされています");
            }

            // 代表者 = 有効チケットの中で最も早く参加した者(方式③は作成者、方式②は参加順先頭、方式①は本人)
            if (!IsRepresentative(ticket, group))
            {
                return Conflict("代表者のスマホから読み取ってください");
            }

            switch (group.Status)
            {
                case GroupStatus.Matching:
                case GroupStatus.Waiting:
                    return Conflict("まだ呼び出されていません");
                case GroupStatus.Completed:
                    // 既にチェックイン済み(冪等)
                    return Ok(new { groupNumber = group.Number, status = group.Status.ToString() });
            }

            var wasInterrupted = group.Status == GroupStatus.Interrupted;
            group.Status = GroupStatus.Completed;
            await groupRepository.SaveChangesAsync();

            if (wasInterrupted)
            {
                // 割り込みpoolのグループ: そろった時点で完了扱いとし、次の呼び出しに割り込んで
                // 処理対象にする(§4.6 優先順位1。チェックイン時点での即時告知として実装)
                await queueCallService.AnnounceAsync(ev, group);
            }
            else
            {
                // 正常キューから呼び出されていたグループのチェックイン完了をトリガーに AutoNext
                await queueCallService.CallNextAsync(ev);
            }
            return Ok(new { groupNumber = group.Number, status = group.Status.ToString() });
        }

        /// <summary>メンバー参加確認画面用: グループ番号・現在人数・満員・参加可否</summary>
        [HttpGet("group/{joinToken}")]
        public async Task<IActionResult> GetGroupInfo(string joinToken)
        {
            var group = await groupRepository.FindByJoinTokenAsync(joinToken);
            if (group == null)
            {
                return NotFound("グループが見つかりません");
            }

            var memberCount = ActiveMemberCount(group);
            return Ok(new
            {
                groupNumber = group.Number,
                memberCount,
                isFull = memberCount >= 3,
                isJoinable = group.Status == GroupStatus.Waiting && memberCount < 3
            });
        }

        /// <summary>
        /// グループ参加(§4.3)。既にどこかに参加済みなら上書き(旧グループ離脱、§4.4)。
        /// 満員・joinToken無効・呼び出し済みは 409。
        /// </summary>
        [HttpPost("group/join")]
        public async Task<IActionResult> GroupJoin([FromBody] GroupJoinRequest request)
        {
            var group = await groupRepository.FindByJoinTokenAsync(request.JoinToken);
            if (group == null)
            {
                // joinToken 無効(代表者離脱による無効化を含む)
                return NotFound("グループが見つかりません");
            }
            if (group.Status != GroupStatus.Waiting)
            {
                return Conflict("このグループには参加できません(呼び出し済み・終了済みです)");
            }
            if (ActiveMemberCount(group) >= 3)
            {
                return Conflict("このグループは既に満員です");
            }

            var ev = await eventRepository.FindByIdAsync(group.EventId);
            if (ev == null)
            {
                return NotFound("イベントが見つかりません");
            }
            if (ev.Status != EventStatus.Open)
            {
                return Conflict("受付終了しました");
            }

            var existing = await ticketRepository.FindActiveByParticipantTokenAsync(
                request.ParticipantToken, ev.DisplayId);
            if (existing != null && existing.ParticipationGroupId == group.Id)
            {
                // 既にこのグループのメンバー → 冪等に現在の券を返す
                return Ok(new { ticketDisplayId = existing.DisplayId.ToString(), groupNumber = group.Number });
            }

            Ticket ticket;
            if (existing != null)
            {
                // 上書き: 旧グループから離脱してチケットを付け替え(§4.4)
                var leaveError = await LeaveCurrentGroupAsync(existing);
                if (leaveError != null)
                {
                    return Conflict(leaveError);
                }
                existing.ParticipationGroupId = group.Id;
                ticket = existing;
            }
            else
            {
                ticket = new Ticket
                {
                    ParticipationGroupId = group.Id,
                    ParticipantToken = request.ParticipantToken
                };
                await ticketRepository.AddAsync(ticket);
            }
            await ticketRepository.SaveChangesAsync();
            await NotifyJoinedAsync(ev);

            return Ok(new { ticketDisplayId = ticket.DisplayId.ToString(), groupNumber = group.Number });
        }

        /// <summary>グループ参加QRのPNG(代表者の電子券画面に表示、設計§8)</summary>
        [HttpGet("group/{joinToken}/qrcode")]
        public async Task<IActionResult> GetGroupQrCode(string joinToken)
        {
            var group = await groupRepository.FindByJoinTokenAsync(joinToken);
            if (group == null || group.JoinToken == null)
            {
                return NotFound("グループが見つかりません");
            }

            var url = $"{ResolveBaseUrl()}/join/{group.JoinToken}";
            return File(qrCodeGenerator.GeneratePng(url, 300, 300), "image/png");
        }

        /// <summary>
        /// 既存の参加から離脱させる(§4.4 上書きルール)。
        /// 呼び出し済み(Calling以降)なら離脱不可としてエラーメッセージを返す。
        /// チケットの付け替え(新グループへの所属変更)は呼び出し側で行う。
        /// </summary>
        private async Task<string?> LeaveCurrentGroupAsync(Ticket ticket)
        {
            if (ticket.ParticipationGroupId == null)
            {
                return null;
            }
            var group = await groupRepository.FindByIdAsync(ticket.ParticipationGroupId.Value);
            if (group == null)
            {
                return null;
            }
            if (group.Status is GroupStatus.Calling or GroupStatus.Interrupted or GroupStatus.Completed)
            {
                return "呼び出し済みのグループのため、参加の変更はできません";
            }

            if (!group.Tickets.Any(t => t.Id != ticket.Id && t.Status != TicketStatus.Cancelled))
            {
                // 残メンバーがいなければグループごとキャンセル
                group.Status = GroupStatus.Cancelled;
            }
            else if (IsRepresentative(ticket, group))
            {
                // 代表者が離脱した場合そのグループのメンバー追加受付は終了(§4.4)
                group.JoinToken = null;
            }
            await groupRepository.SaveChangesAsync();
            return null;
        }

        private static bool IsRepresentative(Ticket ticket, ParticipationGroup group)
        {
            var representative = group.Tickets
                .Where(t => t.Status != TicketStatus.Cancelled)
                .OrderBy(t => t.Created).ThenBy(t => t.Id)
                .FirstOrDefault();
            return representative != null && representative.Id == ticket.Id;
        }

        private static int ActiveMemberCount(ParticipationGroup group)
        {
            return group.Tickets.Count(t => t.Status != TicketStatus.Cancelled);
        }

        private async Task NotifyJoinedAsync(Event ev)
        {
            // 参加登録・上書きは UpdateStatus(参加者画面)と QueueChanged(管理画面)の両方(設計§7)
            await hubContext.Clients.Group(ev.DisplayId.ToString()).SendAsync("UpdateStatus");
            await hubContext.Clients.Group(ev.DisplayId.ToString()).SendAsync("QueueChanged");
        }

        /// <summary>
        /// QRに埋める baseURL(設計§8: 設定 → リクエスト情報。localhost はローカルIPへ変換)。
        /// TicketPdfController のロジックと同じ規則。
        /// </summary>
        private string ResolveBaseUrl()
        {
            var baseUrl = configuration["LotteryBaseUrl"];
            if (!string.IsNullOrEmpty(baseUrl))
            {
                return baseUrl.TrimEnd('/');
            }

            var httpRequest = HttpContext.Request;
            var useHttps = configuration.GetValue<bool?>("UseHttpsForQrCode");
            var scheme = useHttps.HasValue
                ? (useHttps.Value ? "https" : "http")
                : httpRequest.Scheme;
            var host = httpRequest.Host.Host;
            if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase))
            {
                host = GetLocalIPAddress();
            }
            var port = httpRequest.Host.Port ?? (scheme == "https" ? 443 : 80);
            var portString = (scheme == "https" && port != 443) || (scheme == "http" && port != 80)
                ? $":{port}"
                : "";
            return $"{scheme}://{host}{portString}";
        }

        private string GetLocalIPAddress()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    return ip.ToString();
                }
            }
            return "localhost";
        }
    }
}
