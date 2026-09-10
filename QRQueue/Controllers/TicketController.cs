using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QRQueue.Models;
using QRQueue.Models.API;
using System.Security.Claims;

namespace QRQueue.Controllers
{
    [ApiController]
    [Route("api/ticket")]
    public class TicketController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        public TicketController(ApplicationDbContext db)
        {
            _db = db;
        }

        /// <summary>
        /// チケット状態の取得(issue #79)。
        /// 電子チケット(ParticipantToken を持つチケット)は、参加者cookieが一致する本人、
        /// またはログイン済みスタッフのみ取得できる。URLを知っているだけの第三者には
        /// 存在ごと隠す(404)。グループ参加用トークン(joinToken)は本人にのみ返す。
        /// 紙券(ParticipantToken を持たないチケット)は紙の所持が前提のためURLでも
        /// 状態は取得できるが、joinToken は返さない。
        /// </summary>
        [HttpGet("{guid}")]
        public async Task<ActionResult<TicketView>> GetStatus(Guid guid)
        {
            var ticket = await _db.Tickets
                .Include(t => t.ParticipationGroup)
                    .ThenInclude(g => g.Event)
                .Include(t => t.ParticipationGroup)
                    .ThenInclude(g => g.Tickets)
                .FirstOrDefaultAsync(t => t.DisplayId == guid);
            if (ticket == null)
                return NotFound(new ApiMessage("チケットが見つかりません"));

            // 所有確認(issue #79): 参加者cookie(本人 or 同グループの有効チケット保持者) またはスタッフログイン
            // スタッフ判定は既定スキームに依存せず Identity(スタッフ)スキームで明示的に確認する。
            // 参加者cookieは別スキームのため、既定スキームの構成変更で参加者がスタッフ扱いになる事態を防ぐ。
            var participantToken = await ParticipantTokenAsync();
            var isStaff = (await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme)).Succeeded;
            // 本人: 対象チケットの participantToken と一致
            var isOwner = ticket.ParticipantToken != null && ticket.ParticipantToken == participantToken;
            // 同グループの同行者: issue #79 の「同じグループの正当な参加者」も取得対象とする
            var isGroupMember = !isOwner
                && participantToken != null
                && ticket.ParticipantToken != null
                && ticket.ParticipationGroup != null
                && ticket.ParticipationGroup.Tickets.Any(t =>
                    t.Status != TicketStatus.Cancelled && t.ParticipantToken == participantToken);
            if (!isStaff && !isOwner && !isGroupMember && ticket.ParticipantToken != null)
            {
                // 電子チケット: 本人/同行者cookieなしのアクセスは存在も含めて拒否
                return NotFound(new ApiMessage("チケットが見つかりません"));
            }
            // 紙券(ParticipantToken を持たないチケット): 紙の所持が前提のため状態表示は許可する(joinTokenは返さない)

            var group = ticket.ParticipationGroup;
            var ev = group?.Event;

            // 電子券画面用: 代表者判定とグループ参加QR(joinToken)の可否
            // 代表者 = 有効チケットの中で最も早く参加した者(方式③=作成者、方式②=参加順先頭、方式①=本人)
            // joinToken は本人アクセスかつ、受付確定前(Draft)/待機中(Waiting)の場合にのみ返す
            // (代表者がメンバーを追加するため。第三者・スタッフには返さない)
            string? joinToken = null;
            bool isRepresentative = false;
            if (group != null)
            {
                var firstActive = group.Tickets
                    .Where(t => t.Status != TicketStatus.Cancelled)
                    .OrderBy(t => t.Created).ThenBy(t => t.Id)
                    .FirstOrDefault();
                isRepresentative = firstActive != null && firstActive.Id == ticket.Id;
                if (isOwner && isRepresentative && group.Type == GroupType.Manual
                    && group.Status is GroupStatus.Draft or GroupStatus.Waiting)
                {
                    joinToken = group.JoinToken;
                }
            }

            // 受付確定前の管理情報(issue #66)
            var memberCount = group?.Tickets.Count(t => t.Status != TicketStatus.Cancelled);
            var allowCoJoin = group?.AllowCoJoin;

            // 拡張 : 現在の呼び出し番号と自分の順位 aheadCount(前面の Waiting グループ数)
            long? currentCallingNumber = null;
            int? aheadCount = null;
            if (group != null && ev != null && group.Number > 0
                && group.Status is GroupStatus.Waiting or GroupStatus.Calling)
            {
                currentCallingNumber = await _db.ParticipationGroups
                    .Where(g => g.EventId == ev.Id && g.Status == GroupStatus.Calling)
                    .OrderBy(g => g.CalledAt)
                    .Select(g => (long?)g.Number)
                    .FirstOrDefaultAsync();
                aheadCount = await _db.ParticipationGroups.CountAsync(g =>
                    g.EventId == ev.Id && g.Status == GroupStatus.Waiting && g.Number < group.Number);
            }

            return new TicketView(
                group?.Number ?? ticket.Number,
                group?.Status.ToString() ?? ticket.Status.ToString(),
                // チケット自体の状態(使用済みなど、グループ状態とは独立に表示する)(issue #71)
                ticket.Status.ToString(),
                ticket.Status == TicketStatus.Used,
                ev?.DisplayId,
                // === 電子券画面用 拡張項目 ===
                ev?.Name,
                group?.Number,
                currentCallingNumber,
                aheadCount,
                // === 受付確定フロー(issue #66) ===
                memberCount,
                allowCoJoin,
                // === 電子券画面用 接着項目 ===
                joinToken,
                isRepresentative,
                ticket.LineUserId != null);
        }

        /// <summary>
        /// 参加者cookie(§5.2.1)から participantToken を取得。
        /// OnValidatePrincipal でDB照合済みのため、失効トークンは null 扱い。
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
    }
}

