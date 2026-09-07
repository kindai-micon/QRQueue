using Microsoft.AspNetCore.Mvc;
using QRQueue.Models;
using QRQueue.Models.API;
using QRQueue.Services;

namespace QRQueue.Controllers
{
    [Route("api/line")]
    [ApiController]
    public class LineController(ILineService lineService, ApplicationDbContext db) : ControllerBase
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

        // LINE Login の戻り先。紐付けを保存して電子券ページへ戻す
        [HttpGet("callback")]
        public async Task<IActionResult> Callback([FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error)
        {
            var ticketId = (error == null && code != null && state != null)
                ? await lineService.ResolveBindingAsync(code, state)
                : null;

            if (ticketId == null)
            {
                return Redirect("/");
            }
            return Redirect($"/ticket/{ticketId}?line=linked");
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
    }
}
