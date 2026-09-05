namespace QRQueue.Models.API
{
    /// <summary>{ message: "..." } 統一レスポンス(成功メッセージ・エラー共通)</summary>
    public record ApiMessage(string Message);
}
