using QRQueue.Models;

namespace QRQueue.Repositories
{
    /// <summary>
    /// 参加グループのデータアクセス(設計書 §5.2・§5.3)
    /// </summary>
    public interface IParticipationGroupRepository
    {
        Task<ParticipationGroup?> FindByIdAsync(Guid id);

        Task<ParticipationGroup?> FindByDisplayIdAsync(Guid displayId);

        /// <summary>方式③の招待トークンから取得(メンバー参加確認用・チケット読み込み済み)</summary>
        Task<ParticipationGroup?> FindByJoinTokenAsync(string joinToken);

        /// <summary>呼び出し待ち(正常キュー)を番号順に取得</summary>
        Task<List<ParticipationGroup>> GetWaitingAsync(Guid eventId);

        /// <summary>割り込みpoolのグループを退避された順に取得</summary>
        Task<List<ParticipationGroup>> GetInterruptedAsync(Guid eventId);

        /// <summary>方式②のマッチングプール(参加順)</summary>
        Task<List<ParticipationGroup>> GetMatchingPoolAsync(Guid eventId);

        /// <summary>現在呼び出し中のグループ</summary>
        Task<List<ParticipationGroup>> GetCallingAsync(Guid eventId);

        /// <summary>イベント内の採番済み最大呼び出し番号(未採番=0)</summary>
        Task<long> GetMaxNumberAsync(Guid eventId);

        Task AddAsync(ParticipationGroup group);

        void Remove(ParticipationGroup group);

        Task<int> SaveChangesAsync();
    }
}
