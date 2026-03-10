using Billing.Infrastructure.Persistence;
using BuildingBlocks.Application.Interfaces;
using BuildingBlocks.Infrastructure.EfCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Billing.Infrastructure
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddBillingInfrastructure(this IServiceCollection services, string connectionString)
        {
            services.AddDbContext<BillingDbContext>(options => options.UseSqlServer(connectionString));
            services.AddScoped<IUnitOfWork, EfCoreUnitOfWork<BillingDbContext>>();

            return services;
        }
    }
}
