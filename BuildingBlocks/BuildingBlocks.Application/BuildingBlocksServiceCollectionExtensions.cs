using System.Reflection;
using FluentValidation;
using MediatR.Pipeline;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Application;

/// <summary>
/// Extension methods for configuring BuildingBlocks services.
/// </summary>
public static class BuildingBlocksServiceCollectionExtensions
{
    private static bool _fluentValidationDefaultsConfigured;

    /// <summary>
    /// Registers a bounded context's MediatR handlers and FluentValidation validators.
    /// Add additional shared config for contextservices here.
    /// Also configures shared BuildingBlocks defaults (once).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="boundedContextAssembly">The assembly containing handlers and validators.</param>
    public static IServiceCollection AddBoundedContext(this IServiceCollection services, Assembly boundedContextAssembly)
    {
        SetFluentValidationDefaults();

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(boundedContextAssembly);
            cfg.AddOpenBehavior(typeof(RequestPreProcessorBehavior<,>));
        });
           
        services.AddValidatorsFromAssembly(boundedContextAssembly, includeInternalTypes: true);
        

        return services;
    }

    private static void SetFluentValidationDefaults()
    {
        if (_fluentValidationDefaultsConfigured) return;

        // Use property names as-is (no PascalCase to "Display Name" conversion)
        ValidatorOptions.Global.DisplayNameResolver = (type, member, expression) => member?.Name;

        _fluentValidationDefaultsConfigured = true;
    }
}
