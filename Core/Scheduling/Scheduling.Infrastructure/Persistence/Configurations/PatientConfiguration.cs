using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Scheduling.Domain.Patients;

namespace Scheduling.Infrastructure.Persistence.Configurations
{
    public class PatientConfiguration : IEntityTypeConfiguration<Patient>
    {
        public void Configure(EntityTypeBuilder<Patient> builder)
        {
            builder.ToTable("Patients");
            builder.HasKey(p => p.Id);

            #region Conversions
            builder.Property(p => p.Status)
                .IsRequired()
                .HasConversion(
                    status => status.Name,                           // To database: store as string
                    value => PatientStatus.FromName(value, false))   // From database: parse string
                .HasMaxLength(20);
            #endregion Conversions
        }
    }
}
