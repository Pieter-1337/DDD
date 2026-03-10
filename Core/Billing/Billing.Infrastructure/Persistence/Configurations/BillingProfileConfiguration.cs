using Billing.Domain.BillingProfiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Billing.Infrastructure.Persistence.Configurations
{
    public class BillingProfileConfiguration : IEntityTypeConfiguration<BillingProfile>
    {
        public void Configure(EntityTypeBuilder<BillingProfile> builder)
        {
            builder.ToTable("BillingProfiles");
            builder.HasKey(b => b.Id);

            builder.OwnsOne(b => b.PaymentMethod, pm =>
            {
                pm.Property(p => p.Type).HasMaxLength(50).HasColumnName("PaymentMethodType");
                pm.Property(p => p.Last4Digits).HasMaxLength(4).HasColumnName("PaymentMethodLast4");
                pm.Property(p => p.CardholderName).HasMaxLength(200).HasColumnName("PaymentMethodCardholder");
            });

            // Ignore domain events - they are not persisted
            builder.Ignore(p => p.DomainEvents);
        }
    }
}
