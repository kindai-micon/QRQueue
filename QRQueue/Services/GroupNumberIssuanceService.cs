using Microsoft.EntityFrameworkCore;
using QRQueue.Models;

namespace QRQueue.Services;

/// <summary>
/// 先着順呼び出し番号の採番サービス(設計書)。
/// Serializable 分離レベルのトランザクション内で `MAX(Number)+1` 方式を行い、
/// 同時参加の採番競合を防ぐ。開始番号は旧踏襲で 1000 番。
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
    /// <summary>
    /// グループに呼び出し番号を採番する。
    /// 呼び出し元のトランザクションが既にある場合(イベント排他制御内、issue #82)はそれに参加し、
    /// なければ Serializable 分離レベルのトランザクションを開始して採番競合を防ぐ。
    /// </summary>
    public async Task IssueNumberAsync(ParticipationGroup group)
    {
        if (db.Database.CurrentTransaction != null)
        {
            // イベント排他トランザクション(アドバイザリロック保持)に参加して採番する
            await IssueCoreAsync(group);
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable);

        try
        {
            // 採番済み(MAX(Number))の次に繰り上げ。まだ 1 件も採番されていなければ 1000 番開始(旧踏襲)
            var maxNumber = await db.ParticipationGroups
                .Where(g => g.EventId == group.EventId && g.Number > 0)
                .MaxAsync(g => (long?)g.Number);

            group.Number = (maxNumber ?? 999) + 1;

            // 採番とグループ・チケットの保存を一つのトランザクションで確定
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <summary>採番の本体(MAX+1 方式、開始番号は旧踏襲で 1000 番)</summary>
    private async Task IssueCoreAsync(ParticipationGroup group)
    {
        // 採番済み(MAX(Number))の次に繰り上げ。まだ 1 件も採番されていなければ 1000 番開始(旧踏襲)
        var maxNumber = await db.ParticipationGroups
            .Where(g => g.EventId == group.EventId && g.Number > 0)
            .MaxAsync(g => (long?)g.Number);

        group.Number = (maxNumber ?? 999) + 1;

        // 採番とグループ・チケットの保存を一つのトランザクションで確定(§4.5)
        await db.SaveChangesAsync();
    }
}
