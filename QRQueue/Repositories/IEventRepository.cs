using QRQueue.Models;

namespace QRQueue.Repositories
{
    /// <summary>
    /// イベントのデータアクセス(設計書 §5.2)
    /// </summary>
    public interface IEventRepository
    {
        /// <summary>イベント一覧(作成順)</summary>
        Task<List<Event>> ListAsync();

        Task<Event?> FindByIdAsync(Guid id);

        /// <summary>参加登録QRの {eventDisplayId} 用</summary>
        Task<Event?> FindByDisplayIdAsync(Guid displayId);

        Task AddAsync(Event ev);

        void Remove(Event ev);

        Task<int> SaveChangesAsync();
    }
}
