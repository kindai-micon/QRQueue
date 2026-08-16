using Microsoft.EntityFrameworkCore;
using QRQueue.Models;

namespace QRQueue.Repositories.Implementations
{
    public class TicketRepository(ApplicationDbContext applicationDbContext) : ITicketRepository
    {
        public Task<Ticket?> FindByIdAsync(Guid id)
        {
            return applicationDbContext.Tickets.FirstOrDefaultAsync(x => x.Id == id);
        }

        public Task<Ticket?> FindByDisplayIdAsync(Guid displayId)
        {
            return applicationDbContext.Tickets
                .Include(x => x.ParticipationGroup).ThenInclude(x => x.Event)
                .FirstOrDefaultAsync(x => x.DisplayId == displayId);
        }

        public Task<Ticket?> FindActiveByParticipantTokenAsync(Guid participantToken, Guid eventId)
        {
            return applicationDbContext.Tickets
                .Include(x => x.ParticipationGroup).ThenInclude(x => x.Event)
                .Where(x => x.ParticipantToken == participantToken
                    && x.Status == TicketStatus.Registered
                    && x.ParticipationGroup.Event.DisplayId == eventId)
                .OrderByDescending(x => x.Created)
                .FirstOrDefaultAsync();
        }

        public Task<List<Ticket>> GetByEventAsync(Guid eventId)
        {
            return applicationDbContext.Tickets
                .Where(x => x.ParticipationGroup.Event.DisplayId == eventId)
                .OrderBy(x => x.Number)
                .ToListAsync();
        }

        public async Task AddAsync(Ticket ticket)
        {
            await applicationDbContext.Tickets.AddAsync(ticket);
        }

        public void Remove(Ticket ticket)
        {
            applicationDbContext.Tickets.Remove(ticket);
        }

        public Task<int> SaveChangesAsync()
        {
            return applicationDbContext.SaveChangesAsync();
        }
    }
}
