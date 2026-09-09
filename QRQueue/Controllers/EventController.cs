using QRQueue.Models;
using QRQueue.Models.API;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace QRQueue.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class EventController(ApplicationDbContext applicationDbContext) : ControllerBase
    {
        [Authorize]
        [HttpGet(nameof(List))]
        public async Task<ActionResult<List<EventListItem>>> List()
        {
            var list = await applicationDbContext.Events.Select(x => new EventListItem(x.Name, x.DisplayId.ToString())).ToListAsync();
            return list;
        }
        [Authorize(Policy = "EventManagement")]
        [HttpPost(nameof(Create))]
        public async Task<IActionResult> Create([FromBody] string name)
        {
            if (applicationDbContext.Events.Any(x => x.Name == name))
            {
                return BadRequest(new ApiMessage("Event already exists"));
            }
            else
            {
                var ev = new Event()
                {
                    Name = name,
                    TicketInfo = new TicketInfo()
                };

                await applicationDbContext.Events.AddAsync(ev);
                await applicationDbContext.SaveChangesAsync();
            }
            return Ok();
        }
        [Authorize(Policy = "EventManagement")]
        [HttpPost(nameof(Delete))]
        public async Task<IActionResult> Delete([FromBody] string name)
        {
            var ev = await applicationDbContext.Events.FirstOrDefaultAsync(x => x.Name == name);
            if (ev == null)
            {
                return NotFound();
            }
            applicationDbContext.Events.Remove(ev);
            await applicationDbContext.SaveChangesAsync();
            return Ok();
        }
        [Authorize(Policy = "EventManagement")]
        [HttpPut(nameof(Rename))]
        public async Task<IActionResult> Rename([FromBody] RenameModel renameModel)
        {
            var ev = await applicationDbContext.Events.FirstOrDefaultAsync(x => x.Name == renameModel.Name);
            if (ev == null)
            {
                return NotFound();
            }
            ev.Name = renameModel.NewName;
            applicationDbContext.Events.Update(ev);
            await applicationDbContext.SaveChangesAsync();
            return Ok();
        }
        [HttpGet(nameof(Name))]
        public async Task<ActionResult<string>> Name([FromQuery] string id)
        {
            var ev = await applicationDbContext.Events.FirstOrDefaultAsync(x => x.DisplayId.ToString() == id);
            if(ev == null)
            {
                return NotFound();
            }
            return ev.Name;
        }
        [Authorize]

        [HttpPost(nameof(LoadTicketJson))]
        public async Task<IActionResult> LoadTicketJson([FromBody] idAndName idAndName)
        {
            var raw = System.IO.File.ReadAllText(idAndName.json);
            var tmp = JsonSerializer.Deserialize<jsonTicket[]>(raw);
            var ev = await applicationDbContext.Events.Where(x => x.DisplayId.ToString() == idAndName.groupId).FirstOrDefaultAsync();
            foreach(var item in tmp) {
                Ticket ticket = new Ticket();
                ticket.Status = TicketStatus.Registered;
                ticket.Number = item.number;
                ticket.DisplayId = item.displayId;
                applicationDbContext.Tickets.Add(ticket);
            }
            await applicationDbContext.SaveChangesAsync();
            return Ok();
        }

    }
    public record idAndName (string groupId,string json);
    public record jsonTicket(long number,Guid displayId);
}
