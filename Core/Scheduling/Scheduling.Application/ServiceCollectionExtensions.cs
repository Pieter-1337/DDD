using Microsoft.Extensions.DependencyInjection;

namespace Scheduling.Application
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddSchedulingApplication(this IServiceCollection services) 
        {
            services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(ServiceCollectionExtensions).Assembly));
            return services;
        }
    }
}
