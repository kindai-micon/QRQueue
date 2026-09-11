using System.Security.Claims;
using JsxCore;
using JsxCore.Mvc;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using QRQueue.Models;
using QRQueue.Repositories;
using QRQueue.Services;

namespace QRQueue.Controllers
{
    // JsxCore View へのページルーティング(SvelteKit から全面移行)。
    // 旧 Program.cs の MapGet 群を MVC コントローラに集約したもの。
    //
    // レンダリング方針(SSR活用):
    // - 参加/公開ページ は RenderMode.ServerAndClient。コントローラで実データを取得して
    //   モデルの initial に載せることで、初回HTMLに実コンテンツが入る(ファーストペイント高速化)。
    //   ビューは initial を初期状態に使い、以後は SignalR/ポーリングで更新する。
    // - 管理画面系(ログイン後・初期データなし)は Client。SSRが描くのはスケルトンだけで
    //   毎リクエストのサーバー側JS実行と hydration の二重コストを避ける。
    [Controller]
    public class PageController(
        ITicketStatusService ticketStatus,
        IParticipationGroupRepository groups,
        ITicketRepository tickets) : ControllerBase
    {
        private static JsxViewResult Page(string view, object model,
            RenderMode mode = RenderMode.ServerAndClient) =>
            new(view, model, mode);

        // 管理画面系(ログイン必須・初期データはクライアントfetch)は Client で応答する
        private static JsxViewResult AdminPage(string view, object model) =>
            new(view, model, RenderMode.Client);

        [HttpGet("/")]
        public IActionResult Index() => AdminPage("Home/Index", new { });

        [HttpGet("/initial")]
        public IActionResult Initial() => Page("Initial/Index", new { });

        [HttpGet("/login")]
        public IActionResult Login() => Page("Login/Index", new { });

        // ログイン中ユーザー自身のパスワード変更ページ
        [HttpGet("/account/password")]
        public IActionResult ChangePassword() => AdminPage("Account/Password", new { });

        [HttpGet("/roles")]
        public IActionResult Roles() => AdminPage("Roles/Index", new { });

        [HttpGet("/users")]
        public IActionResult Users() => AdminPage("Users/Index", new { });

        [HttpGet("/users/{username}")]
        public IActionResult UserDetail(string username) => AdminPage("Users/Detail", new { username });

        [HttpGet("/admin/delete-data")]
        public IActionResult DeleteData() => AdminPage("Admin/DeleteData", new { });

        [HttpGet("/event")]
        public IActionResult Events() => AdminPage("Event/Index", new { });

        [HttpGet("/event/{eventid}")]
        public IActionResult EventDetail(string eventid) => AdminPage("Event/Detail", new { eventId = eventid });

        [HttpGet("/event/{eventid}/publishing")]
        public IActionResult Publishing(string eventid) => AdminPage("Event/Publishing", new { eventId = eventid });

        [HttpGet("/event/{eventid}/call")]
        public IActionResult Call(string eventid) => AdminPage("Event/Call", new { eventId = eventid });

        [HttpGet("/event/{eventid}/queue")]
        public IActionResult Queue(string eventid) => AdminPage("Event/Queue", new { eventId = eventid });

        // 受付確認QRの自動更新表示(issue #76)
        [HttpGet("/checkin-qr/{eventid}")]
        public IActionResult CheckinQr(string eventid) => Page("Event/CheckinQr", new { eventId = eventid });

        // 参加登録QRの掲示表示(固定QR)
        [HttpGet("/entry-qr/{eventid}")]
        public IActionResult EntryQr(string eventid) => Page("Event/EntryQr", new { eventId = eventid });

        // 電子券ページ(SSR): 本人/同行者/スタッフのときだけ初期データを埋め込む。
        // 認可で弾かれた場合(第三者アクセス)は初期データなしで応答し、
        // クライアントの API 呼び出し(API は 404)と同じ「見つかりません」表示になる。
        [HttpGet("/ticket/{ticketid}")]
        public async Task<IActionResult> Ticket(string ticketid)
        {
            var (token, isStaff) = await ResolveViewerAsync();
            Guid.TryParse(ticketid, out var displayId);
            var initial = await ticketStatus.GetStatusAsync(displayId, token, isStaff);
            return Page("Ticket/Index", new { ticketId = ticketid, initial });
        }

        // グループ参加確認ページ(SSR): 招待QRの飛び先。グループ情報を初期データとして埋め込む。
        [HttpGet("/join/{token}")]
        public async Task<IActionResult> Join(string token)
        {
            Models.API.GroupInfoView? initial = null;
            var group = await groups.FindByJoinTokenAsync(token);
            if (group != null)
            {
                var memberCount = EntryController.ActiveMemberCount(group);
                var isDraft = group.Status == GroupStatus.Draft;
                initial = new Models.API.GroupInfoView(
                    group.Number,
                    memberCount,
                    memberCount >= EntryController.MaxGroupSize,
                    (isDraft || group.Status == GroupStatus.Waiting) && memberCount < EntryController.MaxGroupSize);
            }
            return Page("Entry/Join", new { joinToken = token, initial });
        }

        // チケット引き継ぎ(別端末への復元、issue #75)
        [HttpGet("/transfer")]
        public IActionResult Transfer() => Page("Entry/Transfer", new { });

        [HttpGet("/checkin/{eventid}")]
        public IActionResult Checkin(string eventid) => Page("Entry/Checkin", new { eventDisplayId = eventid });

        // 投影用(旧 /view 置換)
        [HttpGet("/display/{eventid}")]
        public IActionResult Display(string eventid) => Page("Display/Index", new { eventId = eventid });

        // 参加登録ページ。有効な参加者cookieがあれば電子券へ復元リダイレクトする
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

        /// <summary>
        /// 電子券SSR用の閲覧者情報: 参加者cookieの participantToken とスタッフログインの有無。
        /// OnValidatePrincipal でDB照合済みのため、失効トークンは null 扱い。
        /// </summary>
        private async Task<(Guid? participantToken, bool isStaff)> ResolveViewerAsync()
        {
            var participantAuth = await HttpContext.AuthenticateAsync("Participant");
            Guid? token = participantAuth.Principal != null &&
                Guid.TryParse(participantAuth.Principal.FindFirstValue("participantToken"), out var t)
                ? t : null;
            var isStaff = (await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme)).Succeeded;
            return (token, isStaff);
        }
    }
}
