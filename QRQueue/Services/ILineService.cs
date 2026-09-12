namespace QRQueue.Services
{
    public interface ILineService
    {
        /// <summary>LINE連携に必要な設定(ChannelAccessToken / LoginClientId / LoginClientSecret)が揃っているか</summary>
        bool IsConfigured { get; }

        /// <summary>LINE Login 認証画面のURLを生成する(state はチケットDisplayIdの署名付きトークン)</summary>
        string BuildAuthorizeUrl(Guid ticketDisplayId);

        /// <summary>コールバックの code/state を検証してチケットに LineUserId を紐付け、
        /// 紐付いたチケットのDisplayIdを返す。失敗時は TicketDisplayId=null とともに
        /// FailureReason(画面表示用の短い原因コード)を返す。
        /// NotFriend は連携直後の友だち登録状態(未追加=true/追加済みまたは判定不能=null)。
        /// 未追加のまま連携すると通知が届かないため、画面で警告を出せるようにする</summary>
        Task<(Guid? TicketDisplayId, string? FailureReason, bool? NotFriend)> ResolveBindingAsync(string code, string state);

        /// <summary>state(署名付きトークン)からチケットDisplayIdだけを緩く取り出す。
        /// 連携失敗時でも電子券ページへユーザーを戻すために使う(署名・期限の検証はしない)。
        /// チケットDisplayIdは電子券ページのURL自体に使われているため、取り出しても秘匿性の低下はない</summary>
        Guid? ExtractTicketIdFromState(string state);

        /// <summary>チケットと紐付いたLINEユーザーへ呼び出し通知を送る(友だち追加済みのみ届く)</summary>
        Task SendNotifyAsync(IReadOnlyList<Guid> ticketDisplayIds, string text);

        /// <summary>チケットのLINE連携を解除する</summary>
        Task<bool> UnlinkAsync(Guid ticketDisplayId);

        /// <summary>チケット1件宛にテスト通知を実際に送り、結果を診断情報として返す。
        /// 電子券ページのデバッグ用で、シークレットは返さず原因(設定未完了/未連携/友だち未追加/トークン無効)を返す</summary>
        Task<Dictionary<string, object?>> SendTestNotifyAsync(Guid ticketDisplayId);

        /// <summary>LINE連携の設定・資格情報を診断する(一時的な診断用エンドポイント向け)。
        /// シークレットそのものは返さず、設定済みかどうかと検証結果を返す</summary>
        Task<Dictionary<string, object?>> DiagnoseAsync();

        /// <summary>公式アカウントの友だち追加URL(line.me/R/ti/p/@ID)。未設定なら null</summary>
        string? GetAddFriendUrl();

        /// <summary>LINEプラットフォームのWebhookを処理する。
        /// 署名(X-Line-Signature)を検証し、unfollow(ブロック/友だち解除)で連携を自動解除する。
        /// 処理したイベント件数を返す(署名検証失敗時は-1)</summary>
        Task<int> HandleWebhookAsync(string body, string? signature);
    }
}
