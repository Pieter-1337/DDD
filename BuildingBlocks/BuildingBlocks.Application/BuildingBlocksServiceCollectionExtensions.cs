using BuildingBlocks.Application.Behaviors;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace BuildingBlocks.Application;

/// <summary>
/// Extension methods for configuring BuildingBlocks services.
/// </summary>
public static class BuildingBlocksServiceCollectionExtensions
{
    private static bool _fluentValidationDefaultsConfigured;

    /// <summary>
    /// Registers a bounded context's MediatR handlers and FluentValidation validators.
    /// Add additional shared config for context services here.
    /// Also configures shared BuildingBlocks defaults (once).
    /// Does NOT register pipeline behaviors - call AddDefaultPipelineBehaviors() separately.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="boundedContextAssembly">The assembly containing handlers and validators.</param>
    public static IServiceCollection AddBoundedContext(this IServiceCollection services, Assembly boundedContextAssembly)
    {
        SetFluentValidationDefaults();

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(boundedContextAssembly);
        });

        services.AddValidatorsFromAssembly(boundedContextAssembly, includeInternalTypes: true);

        return services;
    }

    /// <summary>
    /// Registers the default pipeline behaviors from BuildingBlocks.
    /// Call this after AddMediatR to add cross-cutting behaviors.
    /// Order matters: behaviors execute in the order they are registered.
    /// </summary>
    public static IServiceCollection AddDefaultPipelineBehaviors(this IServiceCollection services)
    {
        // Order: Logging -> Validation -> Performance -> UnhandledException -> Handler
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(PerformanceBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(UnhandledExceptionBehavior<,>));

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
