using BuildingBlocks.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Billing.Application
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddBillingApplication(this IServiceCollection services)
        {
            services.AddBoundedContext(typeof(ServiceCollectionExtensions).Assembly);

            // Add any Billing-specific services here

            return services;
        }
    }
}
