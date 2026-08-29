using Microsoft.EntityFrameworkCore;
using QRQueue.Models;

namespace QRQueue.Services;

/// <summary>
/// 先着順呼び出し番号の採番サービス(設計 §4.5)。
/// 既存 `TicketIssuanceService` と同じく Serializable 分離レベルのトランザクション内で
/// `MAX(Number)+1` 方式を行い、同時参加の採番競合を防ぐ。開始番号は旧踏襲で 1000 番。
/// </summary>
public interface IGroupNumberIssuanceService
{
    /// <summary>
    /// グループに呼び出し番号を採番し、保留中の変更(グループ・チケットの追加/付け替え)を
    /// 同じ Serializable トランザクション内で確定する。
    /// 呼び出しキューに載るタイミング(方式①参加時・方式③代表者登録時・方式②グループ成立時)で呼ぶ。
    /// </summary>
    Task IssueNumberAsync(ParticipationGroup group);
}

public class GroupNumberIssuanceService(ApplicationDbContext db) : IGroupNumberIssuanceService
{
    public async Task IssueNumberAsync(ParticipationGroup group)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable);

        try
        {
            // 採番済み(MAX(Number))の次に繰り上げ。まだ 1 件も採番されていなければ 1000 番開始(旧踏襲)
            var maxNumber = await db.ParticipationGroups
                .Where(g => g.EventId == group.EventId && g.Number > 0)
                .MaxAsync(g => (long?)g.Number);

            group.Number = (maxNumber ?? 999) + 1;

            // 採番とグループ・チケットの保存を一つのトランザクションで確定(§4.5)
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}
