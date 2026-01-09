using BuildingBlocks.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Scheduling.Application;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSchedulingApplication(this IServiceCollection services)
    {
        services.AddBoundedContext(typeof(ServiceCollectionExtensions).Assembly);

        // Add any Scheduling-specific services here

        return services;
    }
}
