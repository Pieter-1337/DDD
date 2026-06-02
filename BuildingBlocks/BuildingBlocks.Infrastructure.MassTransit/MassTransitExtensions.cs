using BuildingBlocks.Application.Messaging;
using FluentValidation;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Infrastructure.MassTransit.Configuration;

public static class MassTransitExtensions
{
    public static IServiceCollection AddMassTransitEventBus<TDbContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IRegistrationConfigurator>? configureConsumers = null)
        where TDbContext : DbContext
    {
        // Resolve and validate the broker decision through the single
        // broker-selection module (BuildingBlocks.Application.Messaging). It
        // owns reading 'MessageBroker' (defaulting to RabbitMq), resolving the
        // single 'messaging' connection string, and fail-fast format guards.
        // The guards live there, in exactly one place — we never duplicate
        // broker constants, config reading, or connection-string validation here.
        var selection = BrokerSelector.Resolve(configuration);

        services.AddMassTransit(x =>
        {
            // --- Broker-agnostic configuration (shared above the transport branch) ---

            // Allow host to register consumers from specific assemblies
            configureConsumers?.Invoke(x);

            // Configure EF Core Transactional Outbox. The at-least-once delivery
            // guarantee is identical across both brokers, so this stays above
            // the branch.
            x.AddEntityFrameworkOutbox<TDbContext>(o =>
            {
                o.UseSqlServer();
                o.UseBusOutbox();                                   // Intercepts all Publish() calls, not just consumer-scoped ones
                o.QueryDelay = TimeSpan.FromSeconds(5);             // Background delivery polling interval (default: 1 minute)
                o.QueryMessageLimit = 100;                          // Max messages to fetch per poll (default: 100)
            });

            // Retry policy + endpoint wiring are transport-agnostic in MassTransit.
            // Defined once here and applied identically inside whichever transport
            // branch runs, so the retry intervals cannot drift between brokers.
            // Generic over the transport-specific configurator so the
            // strongly-typed ConfigureEndpoints<T> overload can resolve, while
            // the retry policy stays defined in exactly one place.
            static void ConfigureCommon<TEndpointConfigurator>(
                IBusFactoryConfigurator<TEndpointConfigurator> cfg,
                IBusRegistrationContext context)
                where TEndpointConfigurator : IReceiveEndpointConfigurator
            {
                cfg.UseMessageRetry(r =>
                {
                    r.Intervals(
                        TimeSpan.FromSeconds(1),
                        TimeSpan.FromSeconds(5),
                        TimeSpan.FromSeconds(15),
                        TimeSpan.FromSeconds(30)
                    );

                    // Don't retry validation failures
                    r.Ignore<ValidationException>();
                    r.Ignore<ArgumentException>();
                });

                // Configure endpoints for all registered consumers
                cfg.ConfigureEndpoints(context);
            }

            // --- Transport branch (the only broker-specific code) ---
            if (selection.Broker == BrokerNames.AzureServiceBus)
            {
                x.UsingAzureServiceBus((context, cfg) =>
                {
                    // The broker-selection module already validated this is a
                    // Service Bus endpoint connection string. For the Aspire
                    // emulator it is the static emulator string, which carries
                    // 'UseDevelopmentEmulator=true' — the Azure SDK uses that
                    // flag to target the emulator's non-TLS AMQP data port (5672)
                    // instead of the production 443/5671 endpoints. A real Azure
                    // namespace is the same call with a different connection
                    // string supplied via user secrets — zero code change.
                    //
                    // MANAGEMENT-PORT CAVEAT (see PR body / ADR-0001): MassTransit's
                    // startup topology creation uses the Service Bus Administration
                    // Client, whose management operations against the emulator
                    // require the administration port (default 5300) appended to
                    // the host. The data plane uses 5672. A single connection
                    // string carries a single host:port, so we deliberately pass
                    // the Aspire-provided string through UNMODIFIED rather than
                    // append :5300 (which would break the data plane). Whether
                    // 'UseDevelopmentEmulator=true' makes the SDK resolve the
                    // admin port for topology creation is the one item that can
                    // only be confirmed by a live emulator run; it is the headline
                    // manual-QA item. If it fails live, the remedy is a MassTransit
                    // version bump (8.4.0/9.x), NOT switching to cfg.EmulatorHost()
                    // — EmulatorHost() ignores the connection string and would
                    // break the real-namespace zero-code-change override.
                    cfg.Host(selection.ConnectionString);

                    ConfigureCommon(cfg, context);
                });
            }
            else
            {
                x.UsingRabbitMq((context, cfg) =>
                {
                    // The broker-selection module already validated this is an
                    // AMQP URI (e.g. Aspire's amqp://guest:guest@localhost:5672).
                    // The previous 'RabbitMQ:' config-section fallback is gone:
                    // the module now requires the single 'messaging' connection
                    // string for every broker (see PR body — this is a conscious
                    // behavior change; ConnectionStrings:messaging is now required
                    // in non-Aspire environments too).
                    cfg.Host(new Uri(selection.ConnectionString));

                    ConfigureCommon(cfg, context);
                });
            }
        });

        // Register IEventBus implementation
        services.AddScoped<IEventBus, MassTransitEventBus>();

        return services;
    }
}