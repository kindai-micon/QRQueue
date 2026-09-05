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

        public PushSubscriptionController(ApplicationDbContext applicationDbContext,IVapidService vapidService)
        {
            _db = applicationDbContext;
            _service = vapidService;
        }

        [HttpPost("{guid}")]
        public async Task<IActionResult> Subscribe(
            [FromRoute] Guid guid,
            [FromBody] PushSubscriptionDTO subscriptionDTO)
        {
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
