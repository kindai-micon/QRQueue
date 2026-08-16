using Microsoft.EntityFrameworkCore;
using QRQueue.Models;

namespace QRQueue.Repositories.Implementations
{
    public class ParticipationGroupRepository(ApplicationDbContext applicationDbContext) : IParticipationGroupRepository
    {
        public Task<ParticipationGroup?> FindByIdAsync(Guid id)
        {
            return applicationDbContext.ParticipationGroups
                .Include(x => x.Tickets)
                .FirstOrDefaultAsync(x => x.Id == id);
        }

        public Task<ParticipationGroup?> FindByDisplayIdAsync(Guid displayId)
        {
            return applicationDbContext.ParticipationGroups
                .Include(x => x.Tickets)
                .FirstOrDefaultAsync(x => x.DisplayId == displayId);
        }

        public Task<ParticipationGroup?> FindByJoinTokenAsync(string joinToken)
        {
            return applicationDbContext.ParticipationGroups
                .Include(x => x.Tickets)
                .FirstOrDefaultAsync(x => x.JoinToken == joinToken);
        }

        public Task<List<ParticipationGroup>> GetWaitingAsync(Guid eventId)
        {
            return applicationDbContext.ParticipationGroups
                .Where(x => x.EventId == eventId && x.Status == GroupStatus.Waiting)
                .OrderBy(x => x.Number)
                .ToListAsync();
        }

        public Task<List<ParticipationGroup>> GetInterruptedAsync(Guid eventId)
        {
            return applicationDbContext.ParticipationGroups
                .Where(x => x.EventId == eventId && x.Status == GroupStatus.Interrupted)
                .OrderBy(x => x.Updated)
                .ToListAsync();
        }

        public Task<List<ParticipationGroup>> GetMatchingPoolAsync(Guid eventId)
        {
            return applicationDbContext.ParticipationGroups
                .Where(x => x.EventId == eventId && x.Status == GroupStatus.Matching)
                .OrderBy(x => x.Created)
                .ToListAsync();
        }

        public Task<List<ParticipationGroup>> GetCallingAsync(Guid eventId)
        {
            return applicationDbContext.ParticipationGroups
                .Where(x => x.EventId == eventId && x.Status == GroupStatus.Calling)
                .OrderBy(x => x.CalledAt)
                .ToListAsync();
        }

        public async Task<long> GetMaxNumberAsync(Guid eventId)
        {
            return await applicationDbContext.ParticipationGroups
                .Where(x => x.EventId == eventId)
                .MaxAsync(x => (long?)x.Number) ?? 0;
        }

        public async Task AddAsync(ParticipationGroup group)
        {
            await applicationDbContext.ParticipationGroups.AddAsync(group);
        }

        public void Remove(ParticipationGroup group)
        {
            applicationDbContext.ParticipationGroups.Remove(group);
        }

        public Task<int> SaveChangesAsync()
        {
            return applicationDbContext.SaveChangesAsync();
        }
    }
}
