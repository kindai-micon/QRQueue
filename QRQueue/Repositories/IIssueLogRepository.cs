using QRQueue.Models;

namespace QRQueue.Repositories
{
    /// <summary>
    /// 発行ログのデータアクセス
    /// </summary>
    public interface IIssueLogRepository
    {
        /// <summary>イベントの発行ログ(新しい順)</summary>
        Task<List<IssueLog>> GetByEventAsync(Guid eventDisplayId);

        Task AddAsync(IssueLog log);

        Task<int> SaveChangesAsync();
    }
}
