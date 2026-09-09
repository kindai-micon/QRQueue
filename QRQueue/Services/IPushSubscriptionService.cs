using QRQueue.Models;

namespace QRQueue.Services
{
    /// <summary>プッシュ送信の結果報告(テスト通知の応答とログに使う)</summary>
    public record PushSendReport(int Subscriptions, int Sent, IReadOnlyList<string> Errors);

    public interface IPushSubscriptionService
    {
        Task SendNotifyTicketAsync(Ticket ticket, string title, string message);
        Task SendNotifyTicketGroupAsync(List<Ticket> tickets, string title, string message);

        /// <summary>指定チケットの購読へテスト通知を送り、結果を報告する</summary>
        Task<PushSendReport> SendTestAsync(Guid displayId);
    }
}
