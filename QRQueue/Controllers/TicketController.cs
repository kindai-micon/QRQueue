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
                .Include(t => t.ParticipationGroup)
                    .ThenInclude(g => g.Tickets)
                .FirstOrDefaultAsync(t => t.DisplayId == guid);
            if (ticket == null)
                return NotFound("チケットが見つかりません");

            var group = ticket.ParticipationGroup;
            var ev = group?.Event;

            // 電子券画面用(§9.1): 代表者判定とグループ参加QR(joinToken)の可否
            // 代表者 = 有効チケットの中で最も早く参加した者(方式③=作成者、方式②=参加順先頭、方式①=本人)
            string? joinToken = null;
            bool isRepresentative = false;
            if (group != null)
            {
                var firstActive = group.Tickets
                    .Where(t => t.Status != TicketStatus.Cancelled)
                    .OrderBy(t => t.Created).ThenBy(t => t.Id)
                    .FirstOrDefault();
                isRepresentative = firstActive != null && firstActive.Id == ticket.Id;
                if (isRepresentative && group.Type == GroupType.Manual && group.Status == GroupStatus.Waiting)
                {
                    joinToken = group.JoinToken;
                }
            }

            // 拡張 (§6.1): 現在の呼び出し番号と自分の順位 aheadCount(前面の Waiting グループ数)
            long? currentCallingNumber = null;
            int? aheadCount = null;
            if (group != null && ev != null && group.Number > 0
                && group.Status is GroupStatus.Waiting or GroupStatus.Calling)
            {
                currentCallingNumber = await _db.ParticipationGroups
                    .Where(g => g.EventId == ev.Id && g.Status == GroupStatus.Calling)
                    .OrderBy(g => g.CalledAt)
                    .Select(g => (long?)g.Number)
                    .FirstOrDefaultAsync();
                aheadCount = await _db.ParticipationGroups.CountAsync(g =>
                    g.EventId == ev.Id && g.Status == GroupStatus.Waiting && g.Number < group.Number);
            }

            return Ok(new
            {
                number = group?.Number ?? ticket.Number,
                status = group?.Status.ToString() ?? ticket.Status.ToString(),
                eventId = ev?.DisplayId,
                // === 設計§6.1 拡張項目(電子券画面用) ===
                eventName = ev?.Name,
                groupNumber = group?.Number,
                currentCallingNumber,
                aheadCount,
                // === 電子券画面用 接着項目(§9.1) ===
                joinToken,
                isRepresentative
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
