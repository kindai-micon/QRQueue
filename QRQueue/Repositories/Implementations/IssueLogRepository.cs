using Microsoft.EntityFrameworkCore;
using QRQueue.Models;

namespace QRQueue.Repositories.Implementations
{
    public class IssueLogRepository(ApplicationDbContext applicationDbContext) : IIssueLogRepository
    {
        public Task<List<IssueLog>> GetByEventAsync(Guid eventDisplayId)
        {
            return applicationDbContext.IssueLogs
                .Where(x => x.EventDisplayId == eventDisplayId)
                .OrderByDescending(x => x.IssuedAt)
                .ToListAsync();
        }

        public async Task AddAsync(IssueLog log)
        {
            await applicationDbContext.IssueLogs.AddAsync(log);
        }

        public Task<int> SaveChangesAsync()
        {
            return applicationDbContext.SaveChangesAsync();
        }
    }
}
