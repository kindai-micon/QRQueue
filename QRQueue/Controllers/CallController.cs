using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using QRQueue.Hubs;
using QRQueue.Models;
using QRQueue.Models.API;
using QRQueue.Services;
using System.Net.NetworkInformation;

namespace QRQueue.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CallController/*(ApplicationDbContext applicationDbContext)*/ : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IQueueCallService _queueCallService;
        private readonly IPushSubscriptionService _pushSubscriptionService;  //再呼び出し(Again)用.
        private readonly IHubContext<QueueHub> _hubContext;  // 受付状態の即時配信(issue #65)用.
        private readonly ICheckinCodeService _checkinCodeService;
        private readonly IQrCodeGenerator _qrCodeGenerator;
        private readonly IBaseUrlResolver _baseUrlResolver;

        public CallController(
                 ApplicationDbContext db,
                 IQueueCallService queueCallService,
                 IPushSubscriptionService pushSubscriptionService,
                 IHubContext<QueueHub> hubContext,
                 ICheckinCodeService checkinCodeService,
                 IQrCodeGenerator qrCodeGenerator,
                 IBaseUrlResolver baseUrlResolver)
        {
            _db = db;
            _queueCallService = queueCallService;
            _pushSubscriptionService = pushSubscriptionService;
            _hubContext = hubContext;
            _checkinCodeService = checkinCodeService;
            _qrCodeGenerator = qrCodeGenerator;
            _baseUrlResolver = baseUrlResolver;
        }

        /// <summary>
        /// 受付確認QRのPNG(issue #76)。
        /// 30秒で回転する到着確認コードを含むURLのQRを返す。
        /// 受付画面(/checkin-qr/{eventDisplayId})がこの間隔で再読み込みして表示する。
        /// 撮影・共有されたQRはコード失効後に使用できない。
        /// </summary>
        [Authorize(Policy = "CallView")]
        [HttpGet("checkin-qrcode/{eventDisplayId}")]
        public async Task<IActionResult> CheckinQrCode(Guid eventDisplayId)
        {
            var ev = await _db.Events.FirstOrDefaultAsync(x => x.DisplayId == eventDisplayId);
            if (ev == null)
            {
                return NotFound();
            }
            var code = await _checkinCodeService.GetCurrentCheckinCodeAsync(eventDisplayId);
            var url = $"{_baseUrlResolver.Resolve(Request)}/checkin/{eventDisplayId}?rc={code}";
            return File(_qrCodeGenerator.GeneratePng(url, 400, 400), "image/png");
        }

        [Authorize(Policy = "EventOpenClose")]
        [HttpPut("open/{eventDisplayId}")]
        public async Task<IActionResult> Open(Guid eventDisplayId)
        {
            var ev = await _db.Events.FirstOrDefaultAsync(x => x.DisplayId == eventDisplayId);
            if (ev == null)
            {
                return NotFound();
            }
            ev.Status = EventStatus.Open;
            await _db.SaveChangesAsync();
            // 受付状態の変更を参加者画面へ即時配信(issue #65)
            await NotifyStatusChangedAsync(ev.DisplayId);
            return Ok();
        }

        [Authorize(Policy = "EventOpenClose")]
        [HttpPut("close/{eventDisplayId}")]
        public async Task<IActionResult> Close(Guid eventDisplayId)
        {
            var ev = await _db.Events.FirstOrDefaultAsync(x => x.DisplayId == eventDisplayId);
            if (ev == null)
            {
                return NotFound();
            }
            ev.Status = EventStatus.Closed;
            await _db.SaveChangesAsync();
            // 受付状態の変更を参加者画面へ即時配信(issue #65)
            await NotifyStatusChangedAsync(ev.DisplayId);
            return Ok();
        }

        /// <summary>参加登録画面などに受付状態の変化を通知する(issue #65)</summary>
        private Task NotifyStatusChangedAsync(Guid eventDisplayId)
        {
            return _hubContext.Clients.Group(eventDisplayId.ToString()).SendAsync("UpdateStatus");
        }

        [Authorize(Policy = "CallExecute")]
        [HttpPut("next/{eventDisplayId}")]
        public async Task<IActionResult> Next(Guid eventDisplayId)
        {
            // 「次を呼ぶ」は QueueCallService に一本化。
            // 呼出中の未チェックイングループの割込pool退避・方式②プール自動確定・通知もここで行う。
            var ev = await _db.Events.FirstOrDefaultAsync(x => x.DisplayId == eventDisplayId);
            if (ev == null)
            {
                return NotFound();
            }

            var called = await _queueCallService.CallNextAsync(ev);
            if (called == null)
            {
                return NoContent();
            }

            return Ok();
        }

        [Authorize(Policy = "CallExecute")]
        [HttpPut("again/{eventDisplayId}")]
        public async Task<IActionResult> Again(Guid eventDisplayId)
        {
            var callingGroup = await _db.ParticipationGroups
                .Include(x => x.Tickets)
                .FirstOrDefaultAsync(x =>
                x.Event.DisplayId == eventDisplayId &&
                x.Status == GroupStatus.Calling);

            if (callingGroup == null)
            {
                return NotFound();
            }

            if (callingGroup.Tickets.Count == 0)    //下のifでチケットがなかった時を想定.
            {
                return NotFound();
            }
            await _pushSubscriptionService.SendNotifyTicketGroupAsync(callingGroup.Tickets.ToList(), "再度呼び出し", "再度呼び出しが行われました。");
            callingGroup.CallCount++;
            callingGroup.CalledAt = DateTimeOffset.UtcNow;
            

            await _db.SaveChangesAsync();
            
            return Ok();
        }

        [Authorize(Policy = "CallView")]
        [HttpGet("queue/{eventDisplayId}")]
        public async Task<ActionResult<QueueView>> Queue(Guid eventDisplayId)
        {
            var view = new QueueView();

            // 先頭が「次に呼ぶグループ」になるよう番号順に固定(先着順)
            var waitingGroups = await _db.ParticipationGroups.Include(x => x.Tickets).Where(x => x.Event.DisplayId == eventDisplayId && x.Status == GroupStatus.Waiting).OrderBy(x => x.Number).ToListAsync();

            var callingGroups = await _db.ParticipationGroups.Include(x => x.Tickets).Where(x => x.Event.DisplayId == eventDisplayId && x.Status == GroupStatus.Calling).OrderBy(x => x.CalledAt).ToListAsync();

            var interruptedGroups = await _db.ParticipationGroups.Include(x => x.Tickets).Where(x => x.Event.DisplayId == eventDisplayId && x.Status == GroupStatus.Interrupted).OrderBy(x => x.Number).ToListAsync();

            var matchingGroups = await _db.ParticipationGroups.Include(x => x.Tickets).Where(x => x.Event.DisplayId == eventDisplayId && x.Status == GroupStatus.Matching).ToListAsync();

            view.WaitingGroup = waitingGroups.Select(x => new ParticipationGroupView()
            {
                Number = x.Number,
                People = x.Tickets.Count(t => t.Status != TicketStatus.Cancelled),
                Status = x.Status
            });

            view.CallingGroup = callingGroups.Select(x => new ParticipationGroupView()
            {
                Number = x.Number,
                People = x.Tickets.Count(t => t.Status != TicketStatus.Cancelled),
                Status = x.Status
            });

            view.InterruptedGroup = interruptedGroups.Select(x => new ParticipationGroupView()
            {
                Number = x.Number,
                People = x.Tickets.Count(t => t.Status != TicketStatus.Cancelled),
                Status = x.Status
            });

            view.PeoplePool = matchingGroups.Sum(x => x.Tickets.Count(t => t.Status != TicketStatus.Cancelled));

            return view;
        }
    }
}
