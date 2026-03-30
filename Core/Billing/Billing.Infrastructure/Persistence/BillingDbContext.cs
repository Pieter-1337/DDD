using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Billing.Infrastructure.Persistence
{
    public class BillingDbContext : DbContext
    {
        public BillingDbContext(DbContextOptions<BillingDbContext> options)
            : base(options)
        { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(BillingDbContext).Assembly);

            // MassTransit Transactional Outbox tables
            modelBuilder.AddInboxStateEntity(o => o.ToTable("Billing_InboxState"));
            modelBuilder.AddOutboxMessageEntity(o => o.ToTable("Billing_OutboxMessage"));
            modelBuilder.AddOutboxStateEntity(o => o.ToTable("Billing_OutboxState"));
        }
    }
}
