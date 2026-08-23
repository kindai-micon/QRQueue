using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QRQueue.Models;
using QRQueue.Services;
using System.Net.NetworkInformation;

namespace QRQueue.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CallController/*(ApplicationDbContext applicationDbContext)*/ : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public CallController(ApplicationDbContext db)
        {
            _db = db;
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
            ev.Status = EventStatus.Close;
            await _db.SaveChangesAsync();
            return Ok();
        }

        [Authorize(Policy = "CallExecute")]
        [HttpPut("next/{eventDisplayId}")]
        public async Task<IActionResult> Next(Guid eventDisplayId)
        {
            var waitingGroup = await _db.ParticipationGroups.Include(x => x.Tickets).Where(x => x.Event.DisplayId == eventDisplayId && x.Status == GroupStatus.Waiting).OrderBy(x => x.Number).FirstOrDefaultAsync();

            if (waitingGroup == null)
            {
                return NoContent();
            }
            else
            {
                waitingGroup.Status = GroupStatus.Calling;
                await _db.SaveChangesAsync();
            }

            waitingGroup.Status = GroupStatus.Calling;

            await _db.SaveChangesAsync();

            return Ok();
        }

        [Authorize(Policy = "CallExecute")]
        [HttpPut("again/{eventDisplayId}")]
        public async Task<IActionResult> Again(Guid eventDisplayId)
        {
            var callingGroup = await _db.ParticipationGroups.FirstOrDefaultAsync(x => x.Event.DisplayId == eventDisplayId && x.Status == GroupStatus.Calling);

            if (callingGroup == null)
            {
                return NotFound();
            }
            else
            {
                callingGroup.CallCount++;
                callingGroup.CalledAt = DateTimeOffset.UtcNow;
            }

            await _db.SaveChangesAsync();

            return Ok();
        }

        [Authorize(Policy = "CallView")]
        [HttpGet("queue/{eventDisplayId}")]
        public async Task<IActionResult> Queue(Guid eventDisplayId)
        {
            var waitingGroups = await _db.ParticipationGroups.Include(x => x.Tickets).Where(x => x.Event.DisplayId == eventDisplayId && x.Status == GroupStatus.Waiting).ToListAsync();

            var callingGroups = await _db.ParticipationGroups.Include(x => x.Tickets).Where(x => x.Event.DisplayId == eventDisplayId && x.Status == GroupStatus.Calling).ToListAsync();

            var interruptedGroups = await _db.ParticipationGroups.Include(x => x.Tickets).Where(x => x.Event.DisplayId == eventDisplayId && x.Status == GroupStatus.Interrupted).ToListAsync();

            var matchingGroups = await _db.ParticipationGroups.Include(x => x.Tickets).Where(x => x.Event.DisplayId == eventDisplayId && x.Status == GroupStatus.Matching).ToListAsync();

            var waitingQueue = waitingGroups.Select(x => new
            {
                Number = x.Number,
                People = x.Tickets.Count(t => t.Status != TicketStatus.Cancelled),
                Status = x.Status
            });

            var callingQueue = callingGroups.Select(x => new
            {
                Number = x.Number,
                People = x.Tickets.Count(t => t.Status != TicketStatus.Cancelled),
                Status = x.Status
            });

            var interruptedQueue = interruptedGroups.Select(x => new
            {
                Number = x.Number,
                People = x.Tickets.Count(t => t.Status != TicketStatus.Cancelled),
                Status = x.Status
            });

            var poolPeople = matchingGroups.Sum(x => x.Tickets.Count(t => t.Status != TicketStatus.Cancelled));

            return Ok(new
            {
                waiting = waitingQueue,
                calling = callingQueue,
                interrupted = interruptedQueue,
                poolPeople = poolPeople
            });
        }
    }
}
