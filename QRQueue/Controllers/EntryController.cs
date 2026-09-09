using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using QRQueue.Hubs;
using QRQueue.Models;
using QRQueue.Models.API;
using QRQueue.Repositories;
using QRQueue.Services;

namespace QRQueue.Controllers
{
    /// <summary>
    /// 参加者向けAPI(匿名・認証なし)。
    /// 参加登録・電子券の復元・チェックイン・グループ参加。
    /// 本人特定は body にトークンを持たせず、署名付き participantToken cookieから行う。
    /// cookie の検証は `AuthenticateAsync("Participant")` で明示的に行う(既定スキームは Identity のまま)。
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
        IConfiguration configuration,
        ApplicationDbContext applicationDbContext,
        ICheckinCodeService checkinCodeService) : ControllerBase
    {
        /// <summary>1グループの最大参加人数(設計§4)</summary>
        private const int MaxGroupSize = 3;

        public record JoinRequest(Guid EventDisplayId, string Mode, bool Overwrite);
        public record EventRequest(Guid EventDisplayId);
        public record GroupJoinRequest(string JoinToken);
        public record CheckinRequest(Guid EventDisplayId, string? ReceptionCode);

        /// <summary>参加登録画面の初期化(イベント名・受付状態・グループ上限)</summary>
        [HttpGet("{eventDisplayId}")]
        public async Task<ActionResult<EventInfoView>> GetEventInfo(Guid eventDisplayId)
        {
            var ev = await eventRepository.FindByDisplayIdAsync(eventDisplayId);
            if (ev == null)
            {
                return NotFound(new ApiMessage("イベントが見つかりません"));
            }
            return new EventInfoView(
                ev.Name,
                ev.Status,
                ev.Status == EventStatus.Open,
                3);
        }

        /// <summary>
        /// 参加登録(設計書)。mode: solo=即キュー+採番 / pool=マッチングプールへ /
        /// group-create=グループ作成+代表者登録+採番。
        /// 参加者cookie が既存の有効な参加に一致する場合は 409
        /// (クライアントは overwrite フラグで上書き=、または既存券を復元)。
        /// cookie 未保有(初回参加)は成功時に 1 回だけ participantToken cookie を発行する。
        /// </summary>
        [HttpPost("join")]
        public async Task<ActionResult<JoinResult>> Join([FromBody] JoinRequest request)
        {
            var ev = await eventRepository.FindByDisplayIdAsync(request.EventDisplayId);
            if (ev == null)
            {
                return NotFound(new ApiMessage("イベントが見つかりません"));
            }
            if (ev.Status != EventStatus.Open)
            {
                return Conflict(new ApiMessage("受付中ではありません"));
            }

            var cookieToken = await ParticipantTokenAsync();
            var isNewParticipant = cookieToken == null;
            // 端末単位の不変識別子: cookie があればそれを継続、なければ新規発行
            var participantToken = cookieToken ?? Guid.CreateVersion7();

            var existing = await ticketRepository.FindActiveByParticipantTokenAsync(
                participantToken, ev.DisplayId);
            if (existing != null)
            {
                if (!request.Overwrite)
                {
                    return Conflict(new JoinConflict("既に参加登録済みです", existing.DisplayId.ToString()));
                }
                var leaveError = await LeaveCurrentGroupAsync(existing);
                if (leaveError != null)
                {
                    return Conflict(new ApiMessage(leaveError));
                }
            }

            // 上書き時はチケットの付け替え(DisplayId が変わらないため Push 購読も引き継がれる)
            var isNewTicket = existing == null;
            var ticket = existing ?? new Ticket { ParticipantToken = participantToken };

            JoinResult result;
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
                    // 採番(Serializable トランザクション内でグループ・チケットも一緒に保存される)
                    await groupNumberIssuanceService.IssueNumberAsync(group);
                    await NotifyJoinedAsync(ev);
                    result = new JoinResult(ticket.DisplayId.ToString(), group.Number, null);
                    break;
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

                    // 満員成立: プールが設定人数に達したら即座にグループを成立させる
                    var pool = await groupRepository.GetMatchingPoolAsync(ev.Id);
                    ParticipationGroup? formed = null;
                    if (pool.Count >= ev.AutoGroupSize)
                    {
                        formed = await queueCallService.FormGroupFromMatchingPoolAsync(ev, ev.AutoGroupSize);
                    }
                    await NotifyJoinedAsync(ev);

                    result = new JoinResult(
                        ticket.DisplayId.ToString(),
                        formed != null && ticket.ParticipationGroupId == formed.Id ? formed.Number : null,
                        null);
                    break;
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
                    // 代表者登録時点で採番(メンバーが揃うのを待たない)
                    await groupNumberIssuanceService.IssueNumberAsync(group);
                    await NotifyJoinedAsync(ev);
                    result = new JoinResult(ticket.DisplayId.ToString(), group.Number, joinToken);
                    break;
                }
                default:
                    return BadRequest(new ApiMessage("mode は solo / pool / group-create のいずれかを指定してください"));
            }

            // 初回参加の成功時のみ cookie を発行(「発行は1回きり」)
            if (isNewParticipant)
            {
                await IssueParticipantCookieAsync(participantToken);
            }
            return Ok(result);
        }

        /// <summary>同一端末から電子券を復元(参加者cookie → URL喪失対策)</summary>
        [HttpPost("restore")]
        public async Task<ActionResult<RestoreResult>> Restore([FromBody] EventRequest request)
        {
            var ev = await eventRepository.FindByDisplayIdAsync(request.EventDisplayId);
            if (ev == null)
            {
                return NotFound(new ApiMessage("イベントが見つかりません"));
            }

            var participantToken = await ParticipantTokenAsync();
            if (participantToken == null)
            {
                return NotFound(new ApiMessage("参加者cookieがありません"));
            }

            var ticket = await ticketRepository.FindActiveByParticipantTokenAsync(
                participantToken.Value, ev.DisplayId);
            if (ticket == null)
            {
                return NotFound(new ApiMessage("参加登録が見つかりません"));
            }
            return new RestoreResult(ticket.DisplayId.ToString());
        }

        /// <summary>
        /// チェックイン(受付の確定)。参加者cookie で特定した参加者の属するグループが
        /// Calling なら Completed に確定して AutoNext を発火。Interrupted なら同様に完了し、
        /// 次の呼び出しに割り込んで処理対象にする。Waiting/Matching なら 409。
        /// 代表者でない場合も 409。
        /// issue #68: 受付に掲示された確認用QR(到着確認コード付きURL)から開かれた
        /// リクエストのみ受け付ける。確認コードが無効・欠落の場合は理由とともに 403。
        /// </summary>
        [HttpPost("checkin")]
        public async Task<ActionResult<CheckinResult>> Checkin([FromBody] CheckinRequest request)
        {
            var ev = await eventRepository.FindByDisplayIdAsync(request.EventDisplayId);
            if (ev == null)
            {
                return NotFound(new ApiMessage("イベントが見つかりません"));
            }

            // 受付確認用QRの到着確認コード検証(issue #68)
            if (!await checkinCodeService.IsValidAsync(ev.DisplayId, request.ReceptionCode))
            {
                return StatusCode(403,
                    new ApiMessage("受付に掲示された確認用QRコードから開いてください(確認コードが無効か、URLが直接入力されました)"));
            }

            var participantToken = await ParticipantTokenAsync();
            if (participantToken == null)
            {
                return NotFound(new ApiMessage("参加者cookieがありません"));
            }

            var ticket = await ticketRepository.FindActiveByParticipantTokenAsync(
                participantToken.Value, ev.DisplayId);
            if (ticket == null || ticket.ParticipationGroupId == null)
            {
                return NotFound(new ApiMessage("このイベントでの参加登録が見つかりません"));
            }

            var group = await groupRepository.FindByIdAsync(ticket.ParticipationGroupId.Value);
            if (group == null || group.Status == GroupStatus.Cancelled)
            {
                return Conflict(new ApiMessage("この参加はキャンセルされています"));
            }

            // 代表者 = 有効チケットの中で最も早く参加した者(方式③は作成者、方式②は参加順先頭、方式①は本人)
            if (!IsRepresentative(ticket, group))
            {
                return Conflict(new ApiMessage("代表者のスマホから読み取ってください"));
            }

            switch (group.Status)
            {
                case GroupStatus.Matching:
                case GroupStatus.Waiting:
                    return Conflict(new ApiMessage("まだ呼び出されていません"));
                case GroupStatus.Completed:
                    // 既にチェックイン済み(冪等)
                    return new CheckinResult(group.Number, group.Status);
            }

            var wasInterrupted = group.Status == GroupStatus.Interrupted;
            group.Status = GroupStatus.Completed;
            await groupRepository.SaveChangesAsync();

            if (wasInterrupted)
            {
                // 割り込みpoolのグループ: そろった時点で完了扱いとし、次の呼び出しに割り込んで
                // 処理対象にする(優先順位1。チェックイン時点での即時告知として実装)
                await queueCallService.AnnounceAsync(ev, group);
            }
            // issue #70: チェックインを契機とした次グループの自動呼び出し(CallNextAsync)は行わない。
            // 同時に利用できるゲーム枠が1枠のため、到着確認だけが連鎖して呼び出しが進むと
            // 受付場所に待機列ができてしまう。次の呼び出しはスタッフが呼び出しコンソールの
            // 「次を呼ぶ」(PUT /api/call/next/{eventDisplayId})から実行する。
            return new CheckinResult(group.Number, group.Status);
        }

        /// <summary>メンバー参加確認画面用: グループ番号・現在人数・満員・参加可否</summary>
        [HttpGet("group/{joinToken}")]
        public async Task<ActionResult<GroupInfoView>> GetGroupInfo(string joinToken)
        {
            var group = await groupRepository.FindByJoinTokenAsync(joinToken);
            if (group == null)
            {
                return NotFound(new ApiMessage("グループが見つかりません"));
            }

            var memberCount = ActiveMemberCount(group);
            return new GroupInfoView(
                group.Number,
                memberCount,
                memberCount >= 3,
                group.Status == GroupStatus.Waiting && memberCount < 3);
        }

        /// <summary>
        /// グループ参加。参加者cookie で既にどこかに参加中なら上書き(旧グループ離脱)。
        /// 満員・joinToken無効・呼び出し済みは 409。
        /// issue #81: 人数上限(MaxGroupSize)の判定と参加登録を Serializable トランザクション内で
        /// 原子的に行う。同時リクエスト間で人数上限を超えることを防ぎ、
        /// 超過リクエストには分かりやすいエラーを返す。
        /// </summary>
        [HttpPost("group/join")]
        public async Task<ActionResult<JoinResult>> GroupJoin([FromBody] GroupJoinRequest request)
        {
            var preGroup = await groupRepository.FindByJoinTokenAsync(request.JoinToken);
            if (preGroup == null)
            {
                // joinToken 無効(代表者離脱による無効化を含む)
                return NotFound(new ApiMessage("グループが見つかりません"));
            }

            var ev = await eventRepository.FindByIdAsync(preGroup.EventId);
            if (ev == null)
            {
                return NotFound(new ApiMessage("イベントが見つかりません"));
            }

            var cookieToken = await ParticipantTokenAsync();
            var isNewParticipant = cookieToken == null;
            var participantToken = cookieToken ?? Guid.CreateVersion7();

            // ここから原子的処理(issue #81)。Serializable トランザクション内でグループを
            // AsNoTracking で再取得(EF の identity resolution を避け DB の最新値を判定に使う)し、
            // 状態・人数判定を行う。複数の同時参加リクエストは直列化され、
            // 1グループの有効参加者は必ず上限(MaxGroupSize)以下になる。
            await using var transaction = await applicationDbContext.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable);
            try
            {
                var group = await applicationDbContext.ParticipationGroups
                    .AsNoTracking()
                    .Include(g => g.Tickets)
                    .FirstOrDefaultAsync(g => g.Id == preGroup.Id);
                if (group == null)
                {
                    return Conflict(new ApiMessage("このグループには参加できません(終了済みです)"));
                }
                if (group.Status != GroupStatus.Waiting)
                {
                    return Conflict(new ApiMessage("このグループには参加できません(呼び出し済み・終了済みです)"));
                }
                if (ev.Status != EventStatus.Open)
                {
                    return Conflict(new ApiMessage("受付終了しました"));
                }
                if (ActiveMemberCount(group) >= MaxGroupSize)
                {
                    return Conflict(new ApiMessage("このグループは既に満員です。新しいグループを作成して参加してください。"));
                }

                var existing = await ticketRepository.FindActiveByParticipantTokenAsync(
                    participantToken, ev.DisplayId);
                if (existing != null && existing.ParticipationGroupId == group.Id)
                {
                    // 既にこのグループのメンバー → 冪等に現在の券を返す
                    await transaction.CommitAsync();
                    return new JoinResult(existing.DisplayId.ToString(), group.Number, null);
                }

                Ticket ticket;
                if (existing != null)
                {
                    // 上書き: 旧グループから離脱してチケットを付け替え
                    var leaveError = await LeaveCurrentGroupAsync(existing);
                    if (leaveError != null)
                    {
                        await transaction.RollbackAsync();
                        return Conflict(new ApiMessage(leaveError));
                    }
                    existing.ParticipationGroupId = group.Id;
                    ticket = existing;
                }
                else
                {
                    ticket = new Ticket
                    {
                        ParticipationGroupId = group.Id,
                        ParticipantToken = participantToken
                    };
                    await ticketRepository.AddAsync(ticket);
                }
                await ticketRepository.SaveChangesAsync();
                await transaction.CommitAsync();

                // 初回参加の成功時のみ cookie を発行(「発行は1回きり」)
                if (isNewParticipant)
                {
                    await IssueParticipantCookieAsync(participantToken);
                }
                await NotifyJoinedAsync(ev);

                return new JoinResult(ticket.DisplayId.ToString(), group.Number, null);
            }
            catch (Npgsql.NpgsqlException ex) when (ex.SqlState == "40001")
            {
                // Serializable 競合時はコミット時に serialization failure(SQLSTATE 40001)となるため、
                // 500 ではなく超過側に分かりやすい 409 を返す(issue #81 の期待動作)
                await transaction.RollbackAsync();
                return Conflict(new ApiMessage("他の参加と競合してグループが満員になりました。お手数ですが新しいグループを作成して参加してください。"));
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        /// <summary>グループ参加QRのPNG(代表者の電子券画面に表示)</summary>
        [HttpGet("group/{joinToken}/qrcode")]
        public async Task<IActionResult> GetGroupQrCode(string joinToken)
        {
            var group = await groupRepository.FindByJoinTokenAsync(joinToken);
            if (group == null || group.JoinToken == null)
            {
                return NotFound(new ApiMessage("グループが見つかりません"));
            }

            var url = $"{ResolveBaseUrl()}/join/{group.JoinToken}";
            return File(qrCodeGenerator.GeneratePng(url, 300, 300), "image/png");
        }

        /// <summary>
        /// 参加者cookieから participantToken を取得。
        /// 既定スキームは Identity のため、`AuthenticateAsync("Participant")` で明示検証する
        /// (OnValidatePrincipal で DB 照合済み = 失効トークンは null 扱い)。
        /// </summary>
        private async Task<Guid?> ParticipantTokenAsync()
        {
            var auth = await HttpContext.AuthenticateAsync("Participant");
            if (!auth.Succeeded)
            {
                return null;
            }
            return Guid.TryParse(auth.Principal?.FindFirstValue("participantToken"), out var token)
                ? token
                : (Guid?)null;
        }

        /// <summary>初回参加成功時に 1 回だけ署名付き participantToken cookie を発行</summary>
        private async Task IssueParticipantCookieAsync(Guid token)
        {
            var identity = new ClaimsIdentity(
                authenticationType: "Participant",
                nameType: ClaimTypes.Name,
                roleType: ClaimTypes.Role);
            identity.AddClaim(new Claim(ClaimTypes.Name, token.ToString("N")));
            identity.AddClaim(new Claim("participantToken", token.ToString()));
            await HttpContext.SignInAsync(
                "Participant",
                new ClaimsPrincipal(identity),
                new AuthenticationProperties { IsPersistent = true });
        }

        /// <summary>
        /// 既存の参加から離脱させる(上書きルール)。
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
                // 代表者が離脱した場合そのグループのメンバー追加受付は終了
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
            // 参加登録・上書きは UpdateStatus(参加者画面)と QueueChanged(管理画面)の両方(設計書)
            await hubContext.Clients.Group(ev.DisplayId.ToString()).SendAsync("UpdateStatus");
            await hubContext.Clients.Group(ev.DisplayId.ToString()).SendAsync("QueueChanged");
        }

        /// <summary>
        /// QRに埋める baseURL(設定 → リクエスト情報。localhost はローカルIPへ変換)。
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
