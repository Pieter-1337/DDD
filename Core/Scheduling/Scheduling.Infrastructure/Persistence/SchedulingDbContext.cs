using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Scheduling.Infrastructure.Persistence
{
    public class SchedulingDbContext : DbContext
    {
        public SchedulingDbContext(DbContextOptions<SchedulingDbContext> options)
         : base(options)
        { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(SchedulingDbContext).Assembly);

            // MassTransit Transactional Outbox tables
            modelBuilder.AddInboxStateEntity(o => o.ToTable("Scheduling_InboxState"));
            modelBuilder.AddOutboxMessageEntity(o => o.ToTable("Scheduling_OutboxMessage"));
            modelBuilder.AddOutboxStateEntity(o => o.ToTable("Scheduling_OutboxState"));
        }
    }
}
