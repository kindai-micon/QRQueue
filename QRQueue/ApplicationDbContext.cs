using QRQueue.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace QRQueue
{
    public class ApplicationDbContext:IdentityDbContext<ApplicationUser, ApplicationRole, Guid>
    {
        public DbSet<Event> Events { get; set; }
        public DbSet<ParticipationGroup> ParticipationGroups { get; set; }
        public DbSet<Ticket> Tickets { get; set; }
        public DbSet<Authority> Authorities { get; set; }
        public DbSet<IssueLog> IssueLogs { get; set; }
        public DbSet<RefreshToken> RefreshTokens { get; set; }
        public DbSet<PushSubscription> PushSubscriptions { get; set; }
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
        {
        }

        public ApplicationDbContext() : base()
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // イベント削除時にグループ・チケットまで一括削除されるようにする。
            // ParticipationGroupId は nullable(FK)のため既定では CASCADE にならず、
            // 親削除時に FK 違反(23503)でイベント削除が 500 になる
            modelBuilder.Entity<Ticket>()
                .HasOne(t => t.ParticipationGroup)
                .WithMany(g => g.Tickets)
                .HasForeignKey(t => t.ParticipationGroupId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
