using System.Security.Claims;
using JsxCore;
using JsxCore.Mvc;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using QRQueue.Repositories;

namespace QRQueue.Controllers
{
    // JsxCore View へのページルーティング(SvelteKit から全面移行)。
    // 旧 Program.cs の MapGet 群を MVC コントローラに集約したもの。
    [Controller]
    public class PageController(ITicketRepository tickets) : ControllerBase
    {
        private static JsxViewResult Page(string view, object model) =>
            new(view, model, RenderMode.ServerAndClient);

        [HttpGet("/")]
        public IActionResult Index() => Page("Home/Index", new { });

        [HttpGet("/initial")]
        public IActionResult Initial() => Page("Initial/Index", new { });

        [HttpGet("/login")]
        public IActionResult Login() => Page("Login/Index", new { });

        [HttpGet("/roles")]
        public IActionResult Roles() => Page("Roles/Index", new { });

        [HttpGet("/users")]
        public IActionResult Users() => Page("Users/Index", new { });

        [HttpGet("/users/{username}")]
        public IActionResult UserDetail(string username) => Page("Users/Detail", new { username });

        [HttpGet("/admin/delete-data")]
        public IActionResult DeleteData() => Page("Admin/DeleteData", new { });

        [HttpGet("/event")]
        public IActionResult Events() => Page("Event/Index", new { });

        [HttpGet("/event/{eventid}")]
        public IActionResult EventDetail(string eventid) => Page("Event/Detail", new { eventId = eventid });

        [HttpGet("/event/{eventid}/publishing")]
        public IActionResult Publishing(string eventid) => Page("Event/Publishing", new { eventId = eventid });

        [HttpGet("/event/{eventid}/call")]
        public IActionResult Call(string eventid) => Page("Event/Call", new { eventId = eventid });

        [HttpGet("/event/{eventid}/queue")]
        public IActionResult Queue(string eventid) => Page("Event/Queue", new { eventId = eventid });

        [HttpGet("/ticket/{ticketid}")]
        public IActionResult Ticket(string ticketid) => Page("Ticket/Index", new { ticketId = ticketid });

        // 参加者向け匿名ページ(設計§9.1)
        [HttpGet("/join/{token}")]
        public IActionResult Join(string token) => Page("Entry/Join", new { joinToken = token });

        [HttpGet("/checkin/{eventid}")]
        public IActionResult Checkin(string eventid) => Page("Entry/Checkin", new { eventDisplayId = eventid });

        // 投影用(旧 /view 置換)
        [HttpGet("/display/{eventid}")]
        public IActionResult Display(string eventid) => Page("Display/Index", new { eventId = eventid });

        // 参加登録ページ。有効な参加者cookieがあれば電子券へ復元リダイレクトする(§6.1)
        [HttpGet("/entry/{eventid}")]
        public async Task<IActionResult> Entry(string eventid)
        {
            if (Guid.TryParse(eventid, out var eventDisplayId) &&
                (await HttpContext.AuthenticateAsync("Participant")).Principal is { } principal &&
                Guid.TryParse(principal.FindFirstValue("participantToken"), out var participantToken))
            {
                var ticket = await tickets.FindActiveByParticipantTokenAsync(participantToken, eventDisplayId);
                if (ticket != null)
                {
                    return Redirect("/ticket/" + ticket.DisplayId.ToString());
                }
            }
            return Page("Entry/Index", new { eventId = eventid });
        }
    }
}
