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
                .Include(t => t.LotteryGroup)
                .FirstOrDefaultAsync(t => t.DisplayId == guid);
            if (ticket == null)
                return NotFound("チケットが見つかりません");
            return Ok(new
            {
                number = ticket.Number,
                status = ticket.Status.ToString(),
                lotteryGroupId = ticket.LotteryGroup?.DisplayId
            });
        }

        [HttpGet("list")]
        public async Task<IActionResult> GetTickets([FromQuery] Guid lotteryGroupDisplayId)
        {
            // DisplayId から LotteryGroup を取得
            var group = await _db.LotteryGroups
                                 .FirstOrDefaultAsync(g => g.DisplayId == lotteryGroupDisplayId);
            if (group == null)
                return NotFound();

            // チケットと発行ログを内部結合して issuerName を取得
            var tickets = await _db.Tickets
                .Where(t => t.LotteryGroupId == group.Id)
                .Select(t => new {
                    number = t.Number,
                    status = t.Status.ToString(),
                    issuedAt = t.Created,
                    updatedAt = t.Updated,
                    issuerName = _db.IssueLogs
                        .Where(log => log.LotteryGroupDisplayId == lotteryGroupDisplayId
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
