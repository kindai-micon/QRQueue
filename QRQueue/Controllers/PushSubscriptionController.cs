using QRQueue.Models;
using QRQueue.Models.API;
using QRQueue.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography.X509Certificates;

namespace QRQueue.Controllers
{
    [Route("api/push-subscription")]
    [ApiController]
    public class PushSubscriptionController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IVapidService _service;
        private readonly IPushSubscriptionService _pushSubscriptionService;

        public PushSubscriptionController(ApplicationDbContext applicationDbContext,IVapidService vapidService,IPushSubscriptionService pushSubscriptionService)
        {
            _db = applicationDbContext;
            _service = vapidService;
            _pushSubscriptionService = pushSubscriptionService;
        }

        // 実際にプッシュを送らせて端末まで届くかを確かめる(来ない問題の切り分け用)
        [HttpPost("{guid}/test")]
        public async Task<IActionResult> SendTest([FromRoute] Guid guid)
        {
            var report = await _pushSubscriptionService.SendTestAsync(guid);

            if (report.Subscriptions == 0)
            {
                return NotFound(new ApiMessage("このチケットの通知設定がありません。先に「呼び出し通知 登録」を押してください"));
            }
            if (report.Sent == 0)
            {
                return StatusCode(500, new ApiMessage("サーバーからの通知送信に失敗しました: " + string.Join("; ", report.Errors)));
            }
            return Ok(new ApiMessage("テスト通知を送信しました。届かない場合は端末の通知許可・ホーム画面に追加を確認してください"));
        }

        [HttpPost("{guid}")]
        public async Task<IActionResult> Subscribe(
            [FromRoute] Guid guid,
            [FromBody] PushSubscriptionDTO subscriptionDTO)
        {
            // 鍵の無い購読は送信時に必ず失敗するため弾く
            if (string.IsNullOrEmpty(subscriptionDTO?.Endpoint)
                || string.IsNullOrEmpty(subscriptionDTO.Keys?.P256dh)
                || string.IsNullOrEmpty(subscriptionDTO.Keys?.Auth))
            {
                return BadRequest(new ApiMessage("Invalid push subscription"));
            }

            // 同じチケットで再登録されたら上書きし、重複通知を防ぐ
            var existing = await _db.PushSubscriptions
                .FirstOrDefaultAsync(s => s.DisplayId == guid);
            if (existing != null)
            {
                existing.Endpoint = subscriptionDTO.Endpoint;
                existing.P256dh = subscriptionDTO.Keys.P256dh;
                existing.Auth = subscriptionDTO.Keys.Auth;
            }
            else
            {
                _db.PushSubscriptions.Add(new PushSubscription
                {
                    DisplayId = guid,
                    Endpoint = subscriptionDTO.Endpoint,
                    P256dh = subscriptionDTO.Keys.P256dh,
                    Auth = subscriptionDTO.Keys.Auth
                });
            }

            await _db.SaveChangesAsync();

            return Ok();
        }

        [HttpGet("vapid-public-key")]
        public async Task<ActionResult<VapidPublicKeyView>> GetVapidPublicKey()
        {
            var keys = await _service.GetOrCreateKeysAsync();
            if (keys.PublicKey != null && keys.PrivateKey != null)
            {
                return new VapidPublicKeyView(keys.PublicKey);
            }
            return StatusCode(500, new ApiMessage("Push notifications not configured"));
        }
    }
}
