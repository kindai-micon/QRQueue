using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QRQueue.Models;

namespace QRQueue.Controllers
{
    [ApiController]
    [Route("api/ticket")]
    public class TicketController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        public TicketController(ApplicationDbContext db)
        {
            _db = db;
        }

        [HttpGet("{guid}")]
        public async Task<IActionResult> GetStatus(Guid guid)
        {
            var ticket = await _db.Tickets
                .Include(t => t.ParticipationGroup)
                    .ThenInclude(g => g.Event)
                .FirstOrDefaultAsync(t => t.DisplayId == guid);
            if (ticket == null)
                return NotFound("チケットが見つかりません");
            return Ok(new
            {
                number = ticket.ParticipationGroup?.Number ?? ticket.Number,
                status = ticket.ParticipationGroup?.Status.ToString() ?? ticket.Status.ToString(),
                eventId = ticket.ParticipationGroup?.Event?.DisplayId
            });
        }

        [HttpGet("list")]
        public async Task<IActionResult> GetTickets([FromQuery] Guid eventDisplayId)
        {
            // DisplayId から Event を取得
            var ev = await _db.Events
                               .FirstOrDefaultAsync(e => e.DisplayId == eventDisplayId);
            if (ev == null)
                return NotFound();

            // チケットと発行ログを内部結合して issuerName を取得
            var tickets = await _db.Tickets
                .Where(t => t.ParticipationGroup != null && t.ParticipationGroup.EventId == ev.Id)
                .Select(t => new {
                    number = t.ParticipationGroup!.Number,
                    status = t.Status.ToString(),
                    issuedAt = t.Created,
                    updatedAt = t.Updated,
                    issuerName = _db.IssueLogs
                        .Where(log => log.EventDisplayId == eventDisplayId
                                   && t.Number >= log.StartNumber
                                   && t.Number <= log.EndNumber)
                        .Select(log => log.IssuerName)
                        .FirstOrDefault() ?? "—"   // 見つからなければダッシュ
                })
                .OrderBy(x => x.number)
                .ToListAsync();

            return Ok(tickets);
        }

    }
}
