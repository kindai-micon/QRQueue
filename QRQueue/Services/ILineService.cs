namespace QRQueue.Services
{
    public interface ILineService
    {
        /// <summary>LINE連携に必要な設定(ChannelAccessToken / LoginClientId / LoginClientSecret)が揃っているか</summary>
        bool IsConfigured { get; }

        /// <summary>LINE Login 認証画面のURLを生成する(state はチケットDisplayIdの署名付きトークン)</summary>
        string BuildAuthorizeUrl(Guid ticketDisplayId);

        /// <summary>コールバックの code/state を検証してチケットに LineUserId を紐付け、
        /// 紐付けたチケットのDisplayIdを返す(失敗時 null)</summary>
        Task<Guid?> ResolveBindingAsync(string code, string state);

        /// <summary>state(署名付きトークン)からチケットDisplayIdだけを緩く取り出す。
        /// 連携失敗時でも電子券ページへユーザーを戻すために使う(署名・期限の検証はしない)。
        /// チケットDisplayIdは電子券ページのURL自体に使われているため、取り出しても秘匿性の低下はない</summary>
        Guid? ExtractTicketIdFromState(string state);

        /// <summary>チケットと紐付いたLINEユーザーへ呼び出し通知を送る(友だち追加済みのみ届く)</summary>
        Task SendNotifyAsync(IReadOnlyList<Guid> ticketDisplayIds, string text);

        /// <summary>チケットのLINE連携を解除する</summary>
        Task<bool> UnlinkAsync(Guid ticketDisplayId);

        /// <summary>LINE連携の設定・資格情報を診断する(一時的な診断用エンドポイント向け)。
        /// シークレットそのものは返さず、設定済みかどうかと検証結果を返す</summary>
        Task<Dictionary<string, object?>> DiagnoseAsync();
    }
}
