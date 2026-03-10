using Billing.Domain.Invoices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Billing.Infrastructure.Persistence.Configurations;

public class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("Invoices");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Amount).HasPrecision(18, 2);

        builder.Property(i => i.Status)
            .HasConversion(
                s => s.Name,
                s => InvoiceStatus.FromName(s, false));

        builder.Ignore(i => i.DomainEvents);
    }
}
