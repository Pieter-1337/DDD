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
            builder.Ignore(p => p.DomainEvents);

            //#region Validation remove this here later, should be enfored on command when we get to CQRS
            //builder.Property(p => p.FirstName)
            //.IsRequired()
            //.HasMaxLength(100);

            //builder.Property(p => p.LastName)
            //    .IsRequired()
            //    .HasMaxLength(100);

            //builder.Property(p => p.Email)
            //    .IsRequired()
            //    .HasMaxLength(255);

            //builder.HasIndex(p => p.Email)
            //    .IsUnique();

            //builder.Property(p => p.PhoneNumber)
            //    .HasMaxLength(20);

            //builder.Property(p => p.DateOfBirth)
            //    .IsRequired();
            //#endregion Validation remove this here later, should be enfored on command when we get to CQRS

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
