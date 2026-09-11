using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using QRQueue.Models.API;
using QRQueue.Services;
using System.Security.Claims;

namespace QRQueue.Controllers
{
    [ApiController]
    [Route("api/ticket")]
    public class TicketController(ITicketStatusService ticketStatus) : ControllerBase
    {
        /// <summary>
        /// チケット状態の取得(issue #79)。
        /// 電子チケット(ParticipantToken を持つチケット)は、参加者cookieが一致する本人、
        /// またはログイン済みスタッフのみ取得できる。URLを知っているだけの第三者には
        /// 存在ごと隠す(404)。データ取得・認可判定の実体は TicketStatusService。
        /// </summary>
        [HttpGet("{guid}")]
        public async Task<ActionResult<TicketView>> GetStatus(Guid guid)
        {
            var participantToken = await ParticipantTokenAsync();
            var isStaff = (await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme)).Succeeded;
            var view = await ticketStatus.GetStatusAsync(guid, participantToken, isStaff);
            if (view == null)
                return NotFound(new ApiMessage("チケットが見つかりません"));
            return view;
        }

        /// <summary>
        /// 参加者cookie(§5.2.1)から participantToken を取得。
        /// OnValidatePrincipal でDB照合済みのため、失効トークンは null 扱い。
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
    }
}

