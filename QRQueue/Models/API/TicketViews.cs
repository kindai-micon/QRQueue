namespace QRQueue.Models.API
{
    /// <summary>GET /api/ticket/{guid} のレスポンス(電子券状態)。
    /// Status は GroupStatus/TicketStatus のいずれかの文字列。グループ未所属(プール未成立)や
    /// 非代表者では GroupNumber/CurrentCallingNumber/JoinToken 等が null になる。</summary>
    public record TicketView(
        long Number,
        string Status,
        // チケット自体の状態(使用済みなど、グループ状態とは独立に表示する)(issue #71)
        string? TicketStatus,
        bool Used,
        Guid? EventId,
        string? EventName,
        long? GroupNumber,
        long? CurrentCallingNumber,
        int? AheadCount,
        int? MemberCount,
        bool? AllowCoJoin,
        string? JoinToken,
        bool IsRepresentative);
}
