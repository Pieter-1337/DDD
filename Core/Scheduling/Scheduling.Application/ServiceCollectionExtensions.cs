using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Scheduling.Application
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddSchedulingApplication(this IServiceCollection services) 
        {
            var assembly = typeof(ServiceCollectionExtensions).Assembly;

            services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(assembly));
            services.AddValidatorsFromAssembly(assembly);

            return services;
        }
    }
}
