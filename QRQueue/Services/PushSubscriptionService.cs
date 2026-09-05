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

                var vapidSubject = configuration.GetSection("Vapid")["Subject"]
                    ?? "mailto:qrqueue@example.com";    // 未設定でも送信できるようフォールバック(§設定 Vapid:Subject で上書き可)
                var vapidKey = await vapidService.GetOrCreateKeysAsync();

                var vapidDetails = new VapidDetails(
                    vapidSubject,
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
                        url = "/ticket/" + subscription.DisplayId,
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
                    catch (WebPushException ex)
                    {
                        // 購読の無効化(404/410)のときだけ登録を削除。VAPID 設定ミス等の一時的エラーで削除しない
                        if (ex.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Gone)
                        {
                            db.PushSubscriptions.Remove(subscription);
                        }
                        else
                        {
                            Console.WriteLine($"Push send failed ({(int)ex.StatusCode}): {ex.Message}");
                        }
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
