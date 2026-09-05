using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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

        public CallController(
                 ApplicationDbContext db,
                 IQueueCallService queueCallService,
                 IPushSubscriptionService pushSubscriptionService)
        {
            _db = db;
            _queueCallService = queueCallService;
            _pushSubscriptionService = pushSubscriptionService;
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
            return Ok();
        }

        [Authorize(Policy = "CallExecute")]
        [HttpPut("next/{eventDisplayId}")]
        public async Task<IActionResult> Next(Guid eventDisplayId)
        {
            // 「次を呼ぶ」は QueueCallService に一本化(§4.6)。
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

            // 先頭が「次に呼ぶグループ」になるよう番号順に固定(§4.5 先着順)
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
