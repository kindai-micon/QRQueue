using Microsoft.AspNetCore.SignalR;

namespace QRQueue.Hubs
{
    public class QueueHub:Hub
    {
        public async Task SetEvent(string eventId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, eventId);
        }
        public async Task RemoveEvent(string eventId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, eventId);
        }

    }
}
