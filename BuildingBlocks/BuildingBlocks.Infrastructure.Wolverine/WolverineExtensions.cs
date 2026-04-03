using System.Reflection;
using BuildingBlocks.Application.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine;
using Wolverine.RabbitMQ;

namespace BuildingBlocks.Infrastructure.Wolverine;

public static class WolverineExtensions
{
    public static IHostApplicationBuilder AddWolverineEventBus(
        this IHostApplicationBuilder builder,
        Action<WolverineOptions>? configureWolverine = null)
    {
        builder.Services.AddScoped<IEventBus, WolverineEventBus>();

        builder.UseWolverine(opts =>
        {
            var connectionString = builder.Configuration.GetConnectionString("messaging")
                ?? "amqp://guest:guest@localhost:5672";

            opts.UseRabbitMq(new Uri(connectionString))
                .AutoProvision();

            // Only discover handlers where the first parameter implements IIntegrationEvent.
            // This prevents accidental handler registration for non-event types.
            opts.Discovery.CustomizeHandlerDiscovery(q =>
            {
                q.Excludes.WithCondition("Non-IIntegrationEvent handlers", t =>
                    !HasIntegrationEventHandlerMethod(t));
            });

            configureWolverine?.Invoke(opts);
        });

        return builder;
    }

    private static bool HasIntegrationEventHandlerMethod(Type type)
    {
        return type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Any(m =>
                m.Name is "Handle" or "HandleAsync" &&
                m.GetParameters().FirstOrDefault()?.ParameterType
                    .IsAssignableTo(typeof(IIntegrationEvent)) == true);
    }
}
