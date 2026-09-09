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
        IServiceScopeFactory scopeFactory,
        ILogger<PushSubscriptionService> logger) : IPushSubscriptionService
    {
        public async Task SendNotifyTicketGroupAsync(List<Ticket> tickets, string title, string message)
        {
            await SendCoreAsync(tickets.Select(t => t.DisplayId), title, message);
        }

        public Task<PushSendReport> SendTestAsync(Guid displayId)
        {
            return SendCoreAsync([displayId], "テスト通知", "QRQueueのテスト通知です。この通知が届けば設定は完了しています。");
        }

        private async Task<PushSendReport> SendCoreAsync(IEnumerable<Guid> displayIds, string title, string message)
        {
            var errors = new List<string>();
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var vapidSubject = configuration.GetSection("Vapid")["Subject"]
                    ?? "mailto:qrqueue@example.com";    // 未設定でも送信できるようフォールバック(設定 Vapid:Subject で上書き可)
                var vapidKey = await vapidService.GetOrCreateKeysAsync();

                var vapidDetails = new VapidDetails(
                    vapidSubject,
                    vapidKey.PublicKey,
                    vapidKey.PrivateKey
                );

                var webPushClient = new WebPushClient();
                var ids = displayIds.ToList();
                var subscriptions = await db.PushSubscriptions
                    .Where(s => ids.Contains(s.DisplayId))
                    .ToListAsync();

                // 届かないときに原因がまったく分からなくならないよう、件数と成否を必ずログへ出す
                logger.LogInformation("Push: 対象{Count}件に送信を開始 (title={Title})", subscriptions.Count, title);
                if (subscriptions.Count == 0)
                {
                    logger.LogWarning("Push: 送信対象の購読が0件 (displayIds={Ids})", string.Join(",", ids));
                    return new PushSendReport(0, 0, ["このチケットの購読が登録されていません"]);
                }

                var sent = 0;
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
                        sent++;
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
                            logger.LogWarning("Push送信失敗 ({Status}): {Message}", (int)ex.StatusCode, ex.Message);
                            errors.Add($"{(int)ex.StatusCode}: {ex.Message}");
                        }
                    }
                }

                await db.SaveChangesAsync();
                logger.LogInformation("Push: {Sent}/{Count}件を送信", sent, subscriptions.Count);
                return new PushSendReport(subscriptions.Count, sent, errors);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Push送信で予期しないエラー");
                errors.Add(ex.Message);
                return new PushSendReport(0, 0, errors);
            }
        }

        public async Task SendNotifyTicketAsync(Ticket ticket, string title, string message)
        {
            await SendNotifyTicketGroupAsync(new List<Ticket>() { ticket }, title, message);
        }
    }
}
