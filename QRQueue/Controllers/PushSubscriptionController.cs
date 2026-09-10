using QRQueue.Models;
using QRQueue.Models.API;
using QRQueue.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

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
            // チケットの正当な所有者のみテスト通知を送信できる(issue #64/#80)。
            // 検証がないと第三者がチケットIDを知るだけで対象端末へ通知を送れる。
            var ownershipError = await CheckTicketOwnershipAsync(guid);
            if (ownershipError != null)
            {
                return ownershipError;
            }

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

        /// <summary>
        /// 呼び出し通知の購読登録(issue #80)。
        /// リクエスト元の参加者cookie(participantToken)がチケットの所有者と一致する場合のみ
        /// 登録を許可する。有効なチケットIDを知る第三者による他参加者チケットへの購読登録・
        /// 上書きを防止する。
        /// </summary>
        [HttpPost("{guid}")]
        public async Task<IActionResult> Subscribe(
            [FromRoute] Guid guid,
            [FromBody] PushSubscriptionDTO subscriptionDTO)
        {
            var ownershipError = await CheckTicketOwnershipAsync(guid);
            if (ownershipError != null)
            {
                return ownershipError;
            }
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

        /// <summary>
        /// 参加者cookie(§5.2.1)から participantToken を取得する。
        /// OnValidatePrincipal でDB照合済みのため、失効トークンは null 扱いになる。
        /// </summary>
        private async Task<Guid?> ParticipantTokenAsync()
        {
            var auth = await HttpContext.AuthenticateAsync("Participant");
            if (!auth.Succeeded)
            {
                return null;
            }
            return Guid.TryParse(auth.Principal?.FindFirstValue("participantToken"), out var token)
                ? token
                : (Guid?)null;
        }

        /// <summary>
        /// チケット所有権の検証(issue #80)。
        /// 操作可能なのは「参加者cookieと結び付いた自分のチケット」のみ。
        /// 検証に失敗した場合は、ここで適切なステータスコードの応答を返す。
        /// - cookie 未所持: 401
        /// - チケット未存在・所有者不一致: 403(存在の有無を窓口で区別しない)
        /// - 紙券など所有者トークンを持たないチケット: 403(所有確認が成立しないため操作不可)
        /// </summary>
        /// <returns>検証OKの場合は null、失敗の場合は応答</returns>
        private async Task<IActionResult?> CheckTicketOwnershipAsync(Guid ticketDisplayId)
        {
            var participantToken = await ParticipantTokenAsync();
            if (participantToken == null)
            {
                return Unauthorized(new { error = "参加者cookieがありません。参加登録した端末から操作してください。" });
            }

            var ticket = await _db.Tickets.AsNoTracking()
                .FirstOrDefaultAsync(t => t.DisplayId == ticketDisplayId);
            if (ticket == null || ticket.ParticipantToken == null || ticket.ParticipantToken != participantToken)
            {
                return StatusCode(403, new { error = "このチケットに対する操作権限がありません" });
            }

            return null;
        }
    }
}
