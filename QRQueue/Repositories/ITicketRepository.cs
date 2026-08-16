using QRQueue.Models;

namespace QRQueue.Repositories
{
    /// <summary>
    /// チケットのデータアクセス(設計書 §5.2・§6.1)
    /// </summary>
    public interface ITicketRepository
    {
        Task<Ticket?> FindByIdAsync(Guid id);

        /// <summary>電子券URL /ticket/{ticketDisplayId} 用(所属グループ・イベント読み込み済み)</summary>
        Task<Ticket?> FindByDisplayIdAsync(Guid displayId);

        /// <summary>
        /// 参加者cookieの participantToken に一致する「このイベントの」有効なチケットを取得
        /// (二重参加検知 §6.1 join の 409 判定と、電子券の復元で使用)
        /// </summary>
        Task<Ticket?> FindActiveByParticipantTokenAsync(Guid participantToken, Guid eventId);

        /// <summary>イベントのチケット一覧(番号順)</summary>
        Task<List<Ticket>> GetByEventAsync(Guid eventId);

        Task AddAsync(Ticket ticket);

        void Remove(Ticket ticket);

        Task<int> SaveChangesAsync();
    }
}
