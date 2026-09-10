using QRQueue.Models;

namespace QRQueue.Repositories
{
    /// <summary>
    /// チケットのデータアクセス(設計書)
    /// </summary>
    public interface ITicketRepository
    {
        Task<Ticket?> FindByIdAsync(Guid id);

        /// <summary>電子券URL /ticket/{ticketDisplayId} 用(所属グループ・イベント読み込み済み)</summary>
        Task<Ticket?> FindByDisplayIdAsync(Guid displayId);

        /// <summary>
        /// 参加者cookieの participantToken に一致する「このイベントの」有効なチケットを取得
        /// (二重参加検知 join の 409 判定と、電子券の復元で使用)
        /// </summary>
        Task<Ticket?> FindActiveByParticipantTokenAsync(Guid participantToken, Guid eventId);

        /// <summary>
        /// participantToken に一致する有効な参加(Registered かつ所属グループが生きている)が
        /// 全イベント中に存在するか。署名付き cookie の検証用(設計書)。
        /// </summary>
        Task<bool> HasActiveTicketAsync(Guid participantToken);

        /// <summary>
        /// participantToken に一致する有効なチケットを全イベントから取得。
        /// LINE連携コールバックの失敗時、state復元できない場合に参加者cookieから
        /// 戻り先の電子券を特定するために使用する。
        /// </summary>
        Task<List<Ticket>> FindAllActiveByParticipantTokenAsync(Guid participantToken);

        /// <summary>
        /// 引き継ぎコードのハッシュに一致する有効なチケットを取得(issue #75)。
        /// 未期限かつ使用済み(ハッシュ未クリア)のコードにのみ一致する。
        /// </summary>
        Task<Ticket?> FindActiveByTransferCodeAsync(string transferCodeHash);

        /// <summary>イベントのチケット一覧(番号順)</summary>
        Task<List<Ticket>> GetByEventAsync(Guid eventId);

        Task AddAsync(Ticket ticket);

        void Remove(Ticket ticket);

        Task<int> SaveChangesAsync();
    }
}
