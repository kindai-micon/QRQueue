using QRQueue.Models;

namespace QRQueue.Models.API
{
    /// <summary>GET /api/entry/{eventDisplayId} のレスポンス(設計§6.1 イベント情報)</summary>
    public record EventInfoView(string EventName, EventStatus Status, bool IsOpen, int MaxGroupSize);

    /// <summary>参加登録(POST /api/entry/join・/api/entry/group/join)のレスポンス。
    /// joinToken は方式③グループ作成時のみ、groupNumber は方式②プール未成立時 null。</summary>
    public record JoinResult(string TicketDisplayId, long? GroupNumber, string? JoinToken);

    /// <summary>参加登録 409(既に参加済み)のレスポンス。復元用の電子券IDを同梱する(§6.1)</summary>
    public record JoinConflict(string Message, string TicketDisplayId);

    /// <summary>POST /api/entry/restore のレスポンス(cookie からの電子券復元)</summary>
    public record RestoreResult(string TicketDisplayId);

    /// <summary>POST /api/entry/checkin のレスポンス(§4.6 チェックイン)</summary>
    public record CheckinResult(long GroupNumber, GroupStatus Status);

    /// <summary>GET /api/entry/group/{joinToken} のレスポンス(§4.3 グループ情報)</summary>
    public record GroupInfoView(long GroupNumber, int MemberCount, bool IsFull, bool IsJoinable);
}
