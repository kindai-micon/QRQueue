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

        public record JoinRequest(Guid EventDisplayId, string Mode, bool Overwrite, string? ReceptionCode);
        public record EventRequest(Guid EventDisplayId);
        public record GroupJoinRequest(string JoinToken);
        public record CheckinRequest(Guid EventDisplayId, string? ReceptionCode);
        public record TransferStartRequest(Guid EventDisplayId);
        public record TransferCompleteRequest(string Code);

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

            // 受付確認用QRと同じ到着確認コード検証(issue #68 方式の転用)。
            // Web掲示画面(/entry-qr)は30秒で回転するコード、印刷PDFは失効しない固定コード。
            // 撮影・共有されたWeb画面の古いQRからの参加登録は制限される。
            if (!await checkinCodeService.IsValidAsync(ev.DisplayId, request.ReceptionCode))
            {
                return StatusCode(403,
                    new ApiMessage("会場に掲示された参加登録QRコードから開いてください(確認コードが無効か、URLが直接入力されました)"));
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
                        Status = GroupStatus.Waiting,
                        // 「1人で参加」は単独での参加を希望しているため同時参加はオフ(issue #72)
                        AllowCoJoin = false
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
                    // issue #66: 代表者登録時点では「受付確定前(Draft)」とし、
                    // 代表者が「受付」を確定するまで採番・呼び出し対象にしない(§4.3 改め)
                    var joinToken = Guid.CreateVersion7().ToString("N");
                    var group = new ParticipationGroup
                    {
                        EventId = ev.Id,
                        Type = GroupType.Manual,
                        Status = GroupStatus.Draft,
                        JoinToken = joinToken,
                        AllowCoJoin = true // 同時参加可否の初期値はオン(issue #66)
                    };
                    await groupRepository.AddAsync(group);
                    ticket.ParticipationGroupId = group.Id;
                    if (isNewTicket)
                    {
                        await ticketRepository.AddAsync(ticket);
                    }
                    // 受付確定前(Draft)のため採番は行わない。代表者の「受付」確定(GroupConfirm)時に採番する
                    await ticketRepository.SaveChangesAsync();
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
        /// チェックイン成功時はグループのチケットを自動で使用済み(Used)にするとともに、
        /// 参加者cookie を削除する(同じ端末からは新規参加として再登録できる)。
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
                    // 既にチェックイン済み(冪等)。cookie はここで削除する
                    await HttpContext.SignOutAsync("Participant");
                    return new CheckinResult(group.Number, group.Status);
            }

            // 状態遷移(Calling/Interrupted → Completed + 再告知/次の呼び出し)は
            // イベント単位の排他制御下で原子的に確定する(issue #82)。
            // 確定できなかった場合(再読み込みの間に Waiting へ戻された等)は 409。
            var completed = await queueCallService.CompleteCheckinAsync(ev, group.Id);
            if (completed == null)
            {
                // 割り込みpoolのグループ: そろった時点で完了扱いとし、次の呼び出しに割り込んで
                // 処理対象にする(優先順位1。チェックイン時点での即時告知として実装)
                await queueCallService.AnnounceAsync(ev, group);
            }
            // issue #70: チェックインを契機とした次グループの自動呼び出し(CallNextAsync)は行わない。
            // 同時に利用できるゲーム枠が1枠のため、到着確認だけが連鎖して呼び出しが進むと
            // 受付場所に待機列ができてしまう。次の呼び出しはスタッフが呼び出しコンソールの
            // 「次を呼ぶ」(PUT /api/call/next/{eventDisplayId})から実行する。
            // チェックイン成功時は参加者cookieを削除する(チケットは使用済み確定済み。
            // 同じ端末からは新規参加として再登録できる)
            await HttpContext.SignOutAsync("Participant");
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
            // 受付確定前(Draft)・呼び出し待ち(Waiting)のいずれも参加可能(issue #66)
            var isDraft = group.Status == GroupStatus.Draft;
            return new GroupInfoView(
                group.Number,
                memberCount,
                memberCount >= MaxGroupSize,
                (isDraft || group.Status == GroupStatus.Waiting) && memberCount < MaxGroupSize);
        }

        /// <summary>
        /// グループ参加。参加者cookie で既にどこかに参加中なら上書き(旧グループ離脱)。
        /// 満員・joinToken無効・呼び出し済みは 409。
        /// issue #66: 受付確定前(Draft)のグループにも参加できる。
        /// 3人に達した場合、同時参加可否を自動でオフにする。
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
                // joinToken 無効(代表者離脱・受付確定による無効化を含む)
                return NotFound(new ApiMessage("グループが見つかりません"));
            }
            if (preGroup.Status is not (GroupStatus.Draft or GroupStatus.Waiting))
            {
                return Conflict(new ApiMessage("このグループには参加できません(呼び出し済み・終了済みです)"));
            }
            if (ActiveMemberCount(preGroup) >= MaxGroupSize)
            {
                return Conflict(new ApiMessage("このグループは既に満員です。新しいグループを作成して参加してください。"));
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
                // issue #66: 受付確定前(Draft)・呼び出し待ち(Waiting)のいずれも参加可能
                if (group.Status is not (GroupStatus.Draft or GroupStatus.Waiting))
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
                // issue #66: 3人に達した場合は同時参加可否を自動でオフにする。
                // group は AsNoTracking のため条件付き UPDATE で反映する。
                var memberCountAfterAdd = await applicationDbContext.Tickets
                    .CountAsync(t => t.ParticipationGroupId == group.Id
                                  && t.Status != TicketStatus.Cancelled);
                if (memberCountAfterAdd >= MaxGroupSize)
                {
                    await applicationDbContext.ParticipationGroups
                        .Where(g => g.Id == group.Id)
                        .ExecuteUpdateAsync(s => s.SetProperty(g => g.AllowCoJoin, false));
                }
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

        public record CoJoinRequest(Guid EventDisplayId, bool AllowCoJoin);

        /// <summary>
        /// 受付確定(「受付」ボタン、issue #66)。代表者が押した時点で受付を確定し、
        /// 呼び出し番号を採番して待機キューへ追加する。
        /// 確定後は人数・同時参加可否を変更できない(JoinToken を無効化)。
        /// 状態遷移(Waiting 化・JoinToken 無効化)を採番前に済ませ、
        /// IssueNumberAsync と同一トランザクションで原子的に確定する(レビュー指摘)。
        /// 採番後の中断で「番号付きDraft」が残り、再確定で飛び番が発生することを防ぐ。
        /// </summary>
        [HttpPost("group/confirm")]
        public async Task<IActionResult> GroupConfirm([FromBody] EventRequest request)
        {
            var (ev, ticket, group, error) = await FindActiveParticipantAsync(request.EventDisplayId);
            if (error != null)
            {
                return error;
            }

            if (!IsRepresentative(ticket!, group!))
            {
                return StatusCode(403, new ApiMessage("受付の確定は代表者のみが行えます"));
            }
            if (group!.Status != GroupStatus.Draft)
            {
                return Conflict(new ApiMessage("既に受付が確定しているか、取り消されています"));
            }

            // 先に状態遷移を行い(待機キューへ載せ、メンバー追加を受け付けない状態にし)、
            // 採番と併せて IssueNumberAsync の Serializable トランザクション内で原子的に確定する
            group.Status = GroupStatus.Waiting;
            group.JoinToken = null;
            await groupNumberIssuanceService.IssueNumberAsync(group);
            await groupRepository.SaveChangesAsync();
            await NotifyJoinedAsync(ev!);

            return Ok(new { groupNumber = group.Number });
        }

        /// <summary>
        /// 同時参加可否の変更(issue #66)。受付確定前(Draft)の代表者のみ変更できる。
        /// 3人に達したグループは自動でオフになっており変更できない。
        /// </summary>
        [HttpPost("group/cojoin")]
        public async Task<IActionResult> SetCoJoin([FromBody] CoJoinRequest request)
        {
            var (ev, ticket, group, error) = await FindActiveParticipantAsync(request.EventDisplayId);
            if (error != null)
            {
                return error;
            }

            if (!IsRepresentative(ticket!, group!))
            {
                return StatusCode(403, new ApiMessage("同時参加可否の変更は代表者のみが行えます"));
            }
            if (group!.Status != GroupStatus.Draft)
            {
                return Conflict(new ApiMessage("受付確定後は変更できません"));
            }
            if (request.AllowCoJoin && ActiveMemberCount(group) >= MaxGroupSize)
            {
                return Conflict(new ApiMessage("3人に達しているため、同時参加可否をオンにできません"));
            }

            group.AllowCoJoin = request.AllowCoJoin;
            await groupRepository.SaveChangesAsync();
            return Ok(new { allowCoJoin = group.AllowCoJoin });
        }

        /// <summary>
        /// 参加者cookieから、イベント内の有効な参加(チケット)とそのグループを取得する。
        /// 見つからない場合は error に応答を設定して返す。
        /// </summary>
        private async Task<(Event? ev, Ticket? ticket, ParticipationGroup? group, IActionResult? error)>
            FindActiveParticipantAsync(Guid eventDisplayId)
        {
            var ev = await eventRepository.FindByDisplayIdAsync(eventDisplayId);
            if (ev == null)
            {
                return (null, null, null, NotFound(new ApiMessage("イベントが見つかりません")));
            }

            var participantToken = await ParticipantTokenAsync();
            if (participantToken == null)
            {
                return (null, null, null, NotFound(new ApiMessage("参加者cookieがありません")));
            }

            var ticket = await ticketRepository.FindActiveByParticipantTokenAsync(
                participantToken.Value, ev.DisplayId);
            if (ticket == null || ticket.ParticipationGroupId == null)
            {
                return (null, null, null, NotFound(new ApiMessage("このイベントでの参加登録が見つかりません")));
            }

            var group = await groupRepository.FindByIdAsync(ticket.ParticipationGroupId.Value);
            if (group == null)
            {
                return (null, null, null, NotFound(new ApiMessage("このイベントでの参加登録が見つかりません")));
            }

            return (ev, ticket, group, null);
        }
        // ===== チケット引き継ぎ(別端末への復元、issue #75) =====
        // cookie喪失(端末変更・ブラウザ変更・cookie削除等)により電子券へアクセスできなくなった
        // 参加者のための復元手段。元端末で発行した引き継ぎコードを新しい端末で入力すると、
        // チケットの participantToken が付け替えられ、旧端末では当該チケットへアクセスできなくなる
        // (同一チケットの複数端末での重複利用を防止)。引き継ぎ先端末の既存 cookie は尊重し、
        // 別イベントのチケット保有を壊さない。

        /// <summary>引き継ぎコードの有効期間</summary>
        private static readonly TimeSpan TransferCodeLifetime = TimeSpan.FromMinutes(10);

        /// <summary>引き継ぎコードに使用する文字(紛らわしい文字を除外)</summary>
        private const string TransferCodeChars = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

        /// <summary>
        /// 引き継ぎコードの発行(現在の端末=チケット所有者から実行)。
        /// コードは10分間有効なワンタイムで、サーバーにはSHA256ハッシュのみ保存する。
        /// </summary>
        [HttpPost("transfer/start")]
        public async Task<IActionResult> TransferStart([FromBody] TransferStartRequest request)
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
                return NotFound(new ApiMessage("このイベントでの参加登録が見つかりません"));
            }

            var code = GenerateTransferCode();
            ticket.TransferCodeHash = ComputeTransferCodeHash(code);
            ticket.TransferCodeExpiresAt = DateTimeOffset.UtcNow.Add(TransferCodeLifetime);
            await ticketRepository.SaveChangesAsync();

            return Ok(new { code, expiresInMinutes = (int)TransferCodeLifetime.TotalMinutes });
        }

        /// <summary>
        /// 引き継ぎコードによるチケットの復元(新しい端末から実行・cookie不要)。
        /// 成功すると当該チケットの participantToken が付け替えられるため、
        /// 元端末では同じチケットへアクセスできなくなる(重複利用の防止)。
        /// 引き継ぎ先端末が既に参加者cookieを持つ場合はそのトークンを尊重し、
        /// 別イベントの有効チケットとの紐付けを壊さない。
        /// コードの消費は条件付き UPDATE で原子的に行われ、同一コードの並行利用でも
        /// 1回限りの使用が保証される。
        /// </summary>
        [HttpPost("transfer/complete")]
        public async Task<IActionResult> TransferComplete([FromBody] TransferCompleteRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Code))
            {
                return BadRequest(new ApiMessage("引き継ぎコードを入力してください"));
            }

            var hash = ComputeTransferCodeHash(request.Code);
            var ticket = await ticketRepository.FindActiveByTransferCodeAsync(hash);
            if (ticket == null)
            {
                return NotFound(new ApiMessage("引き継ぎコードが無効か、有効期限が切れています。元の端末でコードを発行し直してください。"));
            }

            var evId = ticket.ParticipationGroupId != null
                ? await applicationDbContext.ParticipationGroups
                    .Where(g => g.Id == ticket.ParticipationGroupId)
                    .Select(g => (Guid?)g.EventId)
                    .FirstOrDefaultAsync()
                : null;

            // 引き継ぎ先端末の既存 cookie を上書きしない(レビュー指摘):
            // cookie を新トークンで置き換えると、引き継ぎ先端末が保有する
            // 別イベントの有効チケットとの紐付けが失われるため。
            var existingCookieToken = await ParticipantTokenAsync();
            var targetToken = existingCookieToken ?? Guid.CreateVersion7();

            if (existingCookieToken != null && evId != null)
            {
                // 引き継ぎ先端末が同一イベントの有効チケットを既に持つ場合は重複となるため拒否
                var alreadyJoined = await applicationDbContext.Tickets
                    .AnyAsync(t => t.ParticipantToken == existingCookieToken
                                && t.Status == TicketStatus.Registered
                                && t.ParticipationGroup != null
                                && t.ParticipationGroup.EventId == evId
                                && t.Id != ticket.Id);
                if (alreadyJoined)
                {
                    return Conflict(new ApiMessage("この端末は既にこのイベントに参加済みです。元の端末で参加を解除してからやり直してください。"));
                }
            }

            // コードの消費とトークン付け替えを条件付き UPDATE で原子的に実行する。
            // 同一コードでの並行リクエストでは単一の UPDATE のみ成功し、1回限りの使用を保証する。
            var consumed = await applicationDbContext.Tickets
                .Where(t => t.Id == ticket.Id
                         && t.TransferCodeHash == hash
                         && t.TransferCodeExpiresAt > DateTimeOffset.UtcNow)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.ParticipantToken, targetToken)
                    .SetProperty(t => t.TransferCodeHash, (string?)null)
                    .SetProperty(t => t.TransferCodeExpiresAt, (DateTimeOffset?)null));
            if (consumed == 0)
            {
                // 並行リクエストに先に消費されたか、有効期限が切れている
                return NotFound(new ApiMessage("引き継ぎコードが無効か、有効期限が切れています。元の端末でコードを発行し直してください。"));
            }

            // 引き継ぎ先端末が cookie を持っていなかった場合のみ新規発行
            if (existingCookieToken == null)
            {
                await IssueParticipantCookieAsync(targetToken);
            }

            return Ok(new { ticketDisplayId = ticket.DisplayId.ToString() });
        }

        private static string GenerateTransferCode()
        {
            Span<char> chars = stackalloc char[8];
            for (var i = 0; i < chars.Length; i++)
            {
                chars[i] = TransferCodeChars[System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, TransferCodeChars.Length)];
            }
            return new string(chars);
        }

        private static string ComputeTransferCodeHash(string code)
        {
            var bytes = System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(code.Trim().ToUpperInvariant()));
            return Convert.ToHexString(bytes);
        }

        /// <summary>
        /// グループ全体の受付取消(issue #67)。
        /// 呼び出し前(Waiting/Matching)のグループを、代表者だけが明示的に取り消せる。
        /// 取り消されたグループとチケットは Cancelled となり待機キュー・呼び出し対象から除外され、
        /// 再利用できない。再参加する場合は新しいチケットで受付からやり直す。
        /// 呼び出し後の取り消しはスタッフ操作に限定するため 409 で拒否する。
        /// </summary>
        [HttpPost("group/cancel")]
        public async Task<IActionResult> GroupCancel([FromBody] EventRequest request)
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
            if (ticket == null || ticket.ParticipationGroupId == null)
            {
                return NotFound(new ApiMessage("このイベントでの参加登録が見つかりません"));
            }

            var group = await groupRepository.FindByIdAsync(ticket.ParticipationGroupId.Value);
            if (group == null || group.Status == GroupStatus.Cancelled)
            {
                return NotFound(new ApiMessage("この参加は既に取り消されています"));
            }

            // 代表者以外は取り消せない(issue #67)
            if (!IsRepresentative(ticket, group))
            {
                return StatusCode(403, new ApiMessage("グループの受付取消は代表者のみが行えます"));
            }

            // 呼び出し後(Calling 以降)は参加者画面から取り消せない(issue #67)
            if (group.Status is GroupStatus.Calling or GroupStatus.Interrupted or GroupStatus.Completed)
            {
                return Conflict(new ApiMessage("呼び出し済みのため、参加者画面からは取り消せません。スタッフにお尋ねください。"));
            }

            // グループと有効チケットをすべて無効化し、再利用できないようにする
            group.Status = GroupStatus.Cancelled;
            group.JoinToken = null;
            foreach (var t in group.Tickets.Where(t => t.Status != TicketStatus.Cancelled))
            {
                t.Status = TicketStatus.Cancelled;
            }
            await groupRepository.SaveChangesAsync();
            await NotifyJoinedAsync(ev);

            return Ok(new { groupNumber = group.Number });
        }

        /// <summary>グループ参加QRのPNG(代表者の電子券画面に表示、設計§8)</summary>
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
