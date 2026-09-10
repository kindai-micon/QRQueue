using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using QRQueue.Models;
using QRQueue.Models.API;
using QRQueue.Repositories;
using QRQueue.Services;

namespace QRQueue.Controllers
{
    [Route("api/line")]
    [ApiController]
    public class LineController(
        ILineService lineService,
        ITicketRepository tickets,
        ApplicationDbContext db) : ControllerBase
    {
        // LINE認証画面へリダイレクト(電子券ページの「LINE連携」ボタンの飛び先)
        [HttpGet("authorize/{guid}")]
        public IActionResult Authorize([FromRoute] Guid guid)
        {
            if (!lineService.IsConfigured)
            {
                return NotFound(new ApiMessage("LINE連携は現在設定されていません"));
            }
            return Redirect(lineService.BuildAuthorizeUrl(guid));
        }

        // LINE Login の戻り先。紐付けを保存して電子券ページへ戻す。
        // 失敗時も可能な限り電子券ページへ戻す(ホームはログイン必須のため、
        // 匿名の参加者を / に飛ばすとログイン画面に転送されてしまう)
        [HttpGet("callback")]
        public async Task<IActionResult> Callback([FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error)
        {
            var ticketId = (error == null && code != null && state != null)
                ? await lineService.ResolveBindingAsync(code, state)
                : null;

            if (ticketId != null)
            {
                return Redirect($"/ticket/{ticketId}?line=linked");
            }

            // state からチケットIDが復元できるなら、エラーメッセージ付きで電子券ページへ戻す
            var fallbackTicketId = state != null ? lineService.ExtractTicketIdFromState(state) : null;
            if (fallbackTicketId != null)
            {
                return Redirect($"/ticket/{fallbackTicketId}?line=error");
            }

            // state も復元できない場合は参加者cookie(participantToken)から戻り先を特定する。
            // participant cookie は SameSite=Lax のため、LINE からのトップレベルリダイレクトでも送られる。
            // 有効なチケットが1件だけのときに限り確定できる(複数あると対象を判定できない)
            if ((await HttpContext.AuthenticateAsync("Participant")).Principal is { } principal &&
                Guid.TryParse(principal.FindFirstValue("participantToken"), out var participantToken))
            {
                var active = await tickets.FindAllActiveByParticipantTokenAsync(participantToken);
                if (active.Count == 1)
                {
                    return Redirect($"/ticket/{active[0].DisplayId}?line=error");
                }
            }
            return Redirect("/");
        }

        // LINE連携の解除
        [HttpPost("unlink/{guid}")]
        public async Task<IActionResult> Unlink([FromRoute] Guid guid)
        {
            if (!await lineService.UnlinkAsync(guid))
            {
                return NotFound(new ApiMessage("チケットが見つかりません"));
            }
            return Ok(new ApiMessage("LINE連携を解除しました"));
        }

        // 電子券ページ向け: このチケットのLINE連携の現状(サーバー設定済みか/連携済みか)。
        // 設定未完了の環境で「押しても動かない連携ボタン」を出さず、利用者に無効である旨を
        // 伝えて切り分けられるようにするためのデバッグ情報(シークレットは返さない)
        [HttpGet("status/{guid}")]
        public async Task<IActionResult> Status([FromRoute] Guid guid)
        {
            var lineLinked = await db.Tickets
                .Where(t => t.DisplayId == guid)
                .Select(t => t.LineUserId != null)
                .FirstOrDefaultAsync();
            return Ok(new
            {
                configured = lineService.IsConfigured,
                lineLinked = lineLinked,
            });
        }

        // 電子券ページ向け: 実際にLINEへテスト通知を送って結果を診断する。
        // 友だち追加の解除・トークン無効・サーバー設定漏れなどを、利用者の手元で切り分けられる
        [HttpPost("test/{guid}")]
        public async Task<IActionResult> Test([FromRoute] Guid guid)
        {
            return Ok(await lineService.SendTestNotifyAsync(guid));
        }
    }
}
