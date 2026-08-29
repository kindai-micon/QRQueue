using QRQueue.Models;

namespace QRQueue.Services
{
    public interface IPushSubscriptionService
    {
        Task SendNotifyTicketAsync(Ticket ticket, string title, string message);
        Task SendNotifyTicketGroupAsync(List<Ticket> tickets, string title, string message);
    }
}
