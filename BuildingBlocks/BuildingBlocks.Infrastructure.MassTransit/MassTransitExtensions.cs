using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using BuildingBlocks.Application.Messaging;
using FluentValidation;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using AsbDefaults = MassTransit.AzureServiceBusTransport.Defaults;

namespace BuildingBlocks.Infrastructure.MassTransit.Configuration;

public static class MassTransitExtensions
{
    /// <summary>
    /// Optional second connection string, injected by the Aspire AppHost only on
    /// the Azure Service Bus *emulator* path. It targets the emulator's separate
    /// HTTP management plane (port 5300) for the Service Bus Administration Client,
    /// while the single <c>messaging</c> string (owned by <see cref="BrokerSelector"/>)
    /// targets the AMQP data plane (5672). Kept here, not in <see cref="BrokerSelector"/>,
    /// so the broker-selection module's contract and tests stay untouched; absent it,
    /// the ASB path uses today's single-connection-string wiring (see ADR-0001).
    /// </summary>
    public const string MessagingAdminConnectionStringName = "messaging-admin";

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
                // The Service Bus emulator splits its data plane (AMQP, port 5672)
                // from its management plane (HTTP, port 5300). MassTransit's startup
                // topology creation goes through the Service Bus Administration
                // Client, which speaks HTTP to the management plane — so the
                // administration client needs a connection string pointing at 5300,
                // while the data-plane client uses the 'messaging' string (5672).
                // One connection string carries one host:port, so it cannot serve
                // both planes on the emulator (see ADR-0001).
                //
                // The AppHost injects a second 'messaging-admin' connection string
                // ONLY on the emulator path. When present, we build both clients
                // explicitly and hand them to MassTransit via the
                // Host(Uri, ServiceBusClient, ServiceBusAdministrationClient)
                // overload. When absent (a real Azure namespace, where one endpoint
                // serves both planes, or any non-emulator host), we keep the
                // original single-connection-string Host(connectionString) path —
                // zero behaviour change for real namespaces, preserving the
                // user-secrets 'messaging' override criterion.
                var adminConnectionString = configuration.GetConnectionString(MessagingAdminConnectionStringName);

                x.UsingAzureServiceBus((context, cfg) =>
                {
                    if (string.IsNullOrWhiteSpace(adminConnectionString))
                    {
                        // Real Azure namespace (or any single-plane host): the
                        // 'messaging' endpoint serves both data and management.
                        // A real namespace is supplied via a user-secrets
                        // 'messaging' override with zero code change.
                        cfg.Host(selection.ConnectionString);
                    }
                    else
                    {
                        // The emulator enforces far lower entity limits than the real
                        // service: DefaultMessageTimeToLive must be between 00:00:01
                        // and 01:00:00, while MassTransit's production defaults
                        // (366-day TTL) get rejected with 400 SubCode=40000 at
                        // CreateTopic time. Clamp the transport-wide defaults to the
                        // emulator's ceiling — emulator path only; real namespaces
                        // keep MassTransit's production defaults. This mirrors the
                        // emulator guidance in MassTransit's Azure Service Bus docs.
                        // (Defaults is [EditorBrowsable(Never)] but public — the
                        // documented way to adjust transport-wide entity defaults.)
                        AsbDefaults.DefaultMessageTimeToLive = TimeSpan.FromHours(1);
                        AsbDefaults.BasicMessageTimeToLive = TimeSpan.FromHours(1);
                        AsbDefaults.AutoDeleteOnIdle = TimeSpan.FromHours(1);

                        // Emulator two-plane wiring. The data-plane client targets
                        // 5672 (the 'messaging' string); the administration client
                        // targets 5300 (the 'messaging-admin' string). Both carry
                        // 'UseDevelopmentEmulator=true' so the Azure SDK resolves
                        // the emulator endpoints (and, on Azure.Messaging.ServiceBus
                        // >= 7.20.1, honours the explicit admin port rather than
                        // resetting it). The host Uri is derived from the data-plane
                        // client's fully-qualified namespace, matching the
                        // documented MassTransit pattern for preconfigured clients.
                        var dataClient = new ServiceBusClient(selection.ConnectionString);
                        var adminClient = new ServiceBusAdministrationClient(adminConnectionString);

                        cfg.Host(
                            new Uri($"sb://{dataClient.FullyQualifiedNamespace}"),
                            dataClient,
                            adminClient);
                    }

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