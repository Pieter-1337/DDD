using Identity.WebApi.Models;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Identity.WebApi.Data
{
    public class IdentityDbContext: IdentityDbContext<ApplicationUser>, IDataProtectionKeyContext
    {
        public IdentityDbContext(DbContextOptions<IdentityDbContext> options)
            : base(options)
        {
        }

        public DbSet<DataProtectionKey> DataProtectionKeys { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // Customize table names if needed
            // builder.Entity<ApplicationUser>().ToTable("Users");
            // builder.Entity<IdentityRole>().ToTable("Roles");
            // etc.
        }
    }
}
