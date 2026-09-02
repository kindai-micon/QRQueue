using QRQueue.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using WebPush;

namespace QRQueue.Services
{
    public class PushSubscriptionService(
        IConfiguration configuration,
        IVapidService vapidService,
        IServiceScopeFactory scopeFactory) : IPushSubscriptionService
    {
        public async Task SendNotifyTicketGroupAsync(List<Ticket> tickets, string title, string message)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var vapidSection = configuration.GetSection("Vapid");
                var vapidKey = await vapidService.GetOrCreateKeysAsync();

                var vapidDetails = new VapidDetails(
                    vapidSection["Subject"],
                    vapidKey.PublicKey,
                    vapidKey.PrivateKey
                );

                var webPushClient = new WebPushClient();
                var displayids = tickets.Select(t => t.DisplayId).ToList();
                var subscriptions = await db.PushSubscriptions
                    .Where(s => displayids.Contains(s.DisplayId))
                    .ToListAsync();

                foreach (var subscription in subscriptions)
                {
                    var pushSubscription = new WebPush.PushSubscription(
                        subscription.Endpoint,
                        subscription.P256dh,
                        subscription.Auth
                    );
                    string payload = JsonSerializer.Serialize(new
                    {
                        title = title,
                        body = message,
                        url = "ticket/" + subscription.DisplayId,
                        icon = "./favicon.png"          //変更？
                    });

                    try
                    {
                        await webPushClient.SendNotificationAsync(
                            pushSubscription,
                            payload,
                            vapidDetails
                        );
                    }
                    catch (WebPushException)
                    {
                        db.PushSubscriptions.Remove(subscription);
                    }
                }

                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Push batch error: {ex}");
            }
        }
        public async Task SendNotifyTicketAsync(Ticket ticket, string title, string message)
        {
            await SendNotifyTicketGroupAsync(new List<Ticket>() { ticket }, title, message);
        }
    }
}