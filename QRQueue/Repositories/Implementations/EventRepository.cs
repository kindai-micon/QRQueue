using Microsoft.EntityFrameworkCore;
using QRQueue.Models;

namespace QRQueue.Repositories.Implementations
{
    public class EventRepository(ApplicationDbContext applicationDbContext) : IEventRepository
    {
        public Task<List<Event>> ListAsync()
        {
            return applicationDbContext.Events.OrderBy(x => x.Created).ToListAsync();
        }

        public Task<Event?> FindByIdAsync(Guid id)
        {
            return applicationDbContext.Events.FirstOrDefaultAsync(x => x.Id == id);
        }

        public Task<Event?> FindByDisplayIdAsync(Guid displayId)
        {
            return applicationDbContext.Events.FirstOrDefaultAsync(x => x.DisplayId == displayId);
        }

        public async Task AddAsync(Event ev)
        {
            await applicationDbContext.Events.AddAsync(ev);
        }

        public void Remove(Event ev)
        {
            applicationDbContext.Events.Remove(ev);
        }

        public Task<int> SaveChangesAsync()
        {
            return applicationDbContext.SaveChangesAsync();
        }
    }
}
