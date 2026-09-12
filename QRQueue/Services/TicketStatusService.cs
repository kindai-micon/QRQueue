using Microsoft.EntityFrameworkCore;
using QRQueue.Models;
using QRQueue.Models.API;

namespace QRQueue.Services
{
    /// <summary>
    /// 電子券の状態表示データ(TicketView)の構築。
    /// API(TicketController)と SSR 付きページ(PageController)の両方から使うため
    /// データ取得と認可判定(本人/同行者/スタッフ)をここに集約する。
    /// </summary>
    public interface ITicketStatusService
    {
        /// <summary>
        /// チケット状態を取得。見つからない場合・電子チケットに本人/同行者/スタッフ以外が
        /// アクセスした場合は null(API では 404、ページでは初期データなしで応答)。
        /// </summary>
        Task<TicketView?> GetStatusAsync(Guid displayId, Guid? participantToken, bool isStaff);
    }

    public class TicketStatusService(ApplicationDbContext db) : ITicketStatusService
    {
        public async Task<TicketView?> GetStatusAsync(Guid displayId, Guid? participantToken, bool isStaff)
        {
            var ticket = await db.Tickets
                .Include(t => t.ParticipationGroup)
                    .ThenInclude(g => g.Event)
                .Include(t => t.ParticipationGroup)
                    .ThenInclude(g => g.Tickets)
                .FirstOrDefaultAsync(t => t.DisplayId == displayId);
            if (ticket == null)
                return null;

            // 所有確認(issue #79): 参加者cookie(本人 or 同グループの有効チケット保持者) またはスタッフログイン
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
                return null;
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

            // 拡張 : 現在の整理券番号と自分の順位 aheadCount(前面の Waiting グループ数)
            long? currentCallingNumber = null;
            int? aheadCount = null;
            if (group != null && ev != null && group.Number > 0
                && group.Status is GroupStatus.Waiting or GroupStatus.Calling)
            {
                currentCallingNumber = await db.ParticipationGroups
                    .Where(g => g.EventId == ev.Id && g.Status == GroupStatus.Calling)
                    .OrderBy(g => g.CalledAt)
                    .Select(g => (long?)g.Number)
                    .FirstOrDefaultAsync();
                aheadCount = await db.ParticipationGroups.CountAsync(g =>
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
    }
}
