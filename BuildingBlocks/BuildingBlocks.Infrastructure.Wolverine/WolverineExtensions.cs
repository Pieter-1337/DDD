using System.Reflection;
using BuildingBlocks.Application.Interfaces;
using BuildingBlocks.Application.Messaging;
using FluentValidation;
using JasperFx;
using JasperFx.CodeGeneration.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine;
using Wolverine.AzureServiceBus;
using Wolverine.EntityFrameworkCore;
using Wolverine.ErrorHandling;
using Wolverine.RabbitMQ;
using Wolverine.SqlServer;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("BuildingBlocks.Tests")]

namespace BuildingBlocks.Infrastructure.Wolverine;

public static class WolverineExtensions
{
    // Flows the resolved broker to ListenToMassTransitQueue<T>, which the host calls from
    // inside the configureWolverine callback. WolverineOptions exposes no config at this
    // package version, and host configuration is synchronous, so an ambient slot (set inside
    // the UseWolverine lambda, reset in finally) is how the helper learns the broker.
    private static readonly AsyncLocal<string?> CurrentBroker = new();

    /// <summary>
    /// Optional second connection string, injected by the Aspire AppHost only on the
    /// Azure Service Bus *emulator* path. It targets the emulator's separate HTTP
    /// management plane (port 5300) for topology provisioning, while the single
    /// <c>messaging</c> string (owned by <see cref="BrokerSelector"/>) targets the AMQP
    /// data plane (5672). Same key the MassTransit extension reads — both frameworks
    /// consume the one value the AppHost fans out (see ADR-0001/ADR-0003).
    /// </summary>
    internal const string MessagingAdminConnectionStringName = "messaging-admin";

    public static IHostApplicationBuilder AddWolverineEventBus<TDbContext>(
        this IHostApplicationBuilder builder,
        string dbConnectionString,
        string schemaName,
        Action<WolverineOptions>? configureWolverine = null)
        where TDbContext : DbContext
    {
        // Single source for the broker decision: reads 'MessageBroker', resolves the
        // 'messaging' connection string, and runs the format guards (see ADR-0001).
        // Note: the old 'RabbitMQ:' config-section fallback is gone — 'messaging' is
        // now required for every broker, including non-Aspire environments.
        var selection = BrokerSelector.Resolve(builder.Configuration);

        // Captured here (outside the host-build lambda, which runs after this method
        // returns) so the ASB branch can wire the emulator's management plane. Null on
        // RabbitMQ and on real Azure namespaces (one endpoint serves both planes there).
        var adminConnectionString = builder.Configuration.GetConnectionString(MessagingAdminConnectionStringName);

        builder.Services.AddScoped<IEventBus, WolverineDbContextEventBus<TDbContext>>();
        builder.Services.AddScoped<ICommitStrategy, WolverineCommitStrategy<TDbContext>>();
        builder.Services.AddScoped(typeof(IDbContextOutbox<TDbContext>), typeof(DbContextOutbox<TDbContext>));

        builder.UseWolverine(opts =>
        {
            // --- Transport branch (the only broker-specific code) ---
            if (selection.Broker == BrokerNames.AzureServiceBus)
            {
                // Emulator only: topology provisioning uses a separate HTTP management
                // port the 'messaging' data-plane string can't reach, so wire the admin
                // string below. Null (and skipped) on RabbitMQ / real namespaces, where one
                // endpoint serves both planes. See ADR-0003.
                opts.UseAzureServiceBus(selection.ConnectionString)
                    .AutoProvision()
                    .UseConventionalRouting();

                if (!string.IsNullOrWhiteSpace(adminConnectionString))
                {
                    opts.Transports.GetOrCreate<AzureServiceBusTransport>()
                        .ManagementConnectionString = adminConnectionString;
                }
            }
            else
            {
                // The broker-selection module already validated this is an AMQP URI
                // (e.g. Aspire's amqp://guest:guest@localhost:5672).
                opts.UseRabbitMq(new Uri(selection.ConnectionString))
                    .AutoProvision()
                    .UseConventionalRouting();
            }

            // --- Broker-agnostic configuration (shared above the transport branch) ---

            // Wolverine 6 flipped the default ServiceLocationPolicy to NotAllowed, which
            // rejects our consumers' IMediator dispatch (MediatR resolves via IServiceProvider).
            // Restore the 5.x default; AllowedButWarn keeps the reliance visible in logs. See ADR-0003.
            opts.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;

            // Only discover handlers where the first parameter implements IIntegrationEvent.
            // This prevents accidental handler registration for non-event types.
            opts.Discovery.CustomizeHandlerDiscovery(q =>
            {
                q.Excludes.WithCondition("Non-IIntegrationEvent handlers", t =>
                    !HasIntegrationEventHandlerMethod(t));
            });

            // Configure transactional outbox with SQL Server (per-BC schema).
            // The at-least-once delivery guarantee is identical across both brokers.
            opts.PersistMessagesWithSqlServer(dbConnectionString, schemaName);
            opts.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;

            // Use EF Core transactions for atomic outbox
            opts.UseEntityFrameworkCoreTransactions();

            // Retry intervals match the MassTransit extension; global on 'opts' so
            // they're identical across both brokers.
            opts.OnException<ValidationException>().MoveToErrorQueue();
            opts.OnException<ArgumentException>().MoveToErrorQueue();
            opts.OnAnyException().RetryWithCooldown(
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(15),
                TimeSpan.FromSeconds(30));

            // Flow the broker to the interop helper for the duration of the host's
            // callback (see CurrentBroker). Set here, not in the method body, because
            // this lambda runs at host-build time — after the body has returned.
            CurrentBroker.Value = selection.Broker;
            try
            {
                configureWolverine?.Invoke(opts);
            }
            finally
            {
                CurrentBroker.Value = null;
            }
        });

        return builder;
    }

    /// <summary>
    /// Listens to a MassTransit-published message by binding a queue to MassTransit's exchange
    /// and configuring MassTransit envelope deserialization.
    /// </summary>
    /// <remarks>
    /// RabbitMQ-only: built on RabbitMQ exchange semantics with no Azure Service Bus
    /// equivalent (ADR-0001), so it fails fast on Azure Service Bus rather than silently
    /// receiving nothing.
    /// </remarks>
    public static WolverineOptions ListenToMassTransitQueue<TMessage>(
        this WolverineOptions opts,
        string queueName)
    {
        GuardMassTransitInteropSupported(CurrentBroker.Value);

        // MassTransit exchange naming convention: "Namespace:TypeName"
        var exchangeName = $"{typeof(TMessage).Namespace}:{typeof(TMessage).Name}";

        opts.UseRabbitMq()
            .BindExchange(exchangeName)
            .ToQueue(queueName);

        opts.ListenToRabbitQueue(queueName)
            .DefaultIncomingMessage<TMessage>()
            .UseMassTransitInterop();

        return opts;
    }

    /// <summary>
    /// Publishes messages to a MassTransit consumer by routing to MassTransit's exchange
    /// and wrapping messages in MassTransit's envelope format.
    /// </summary>
    public static WolverineOptions PublishToMassTransitExchange<TMessage>(
        this WolverineOptions opts)
    {
        // MassTransit exchange naming convention: "Namespace:TypeName"
        var exchangeName = $"{typeof(TMessage).Namespace}:{typeof(TMessage).Name}";

        opts.PublishMessage<TMessage>()
            .ToRabbitExchange(exchangeName)
            .UseMassTransitInterop();

        return opts;
    }

    /// <summary>
    /// Fails fast when the MassTransit-interop listener is configured on Azure Service Bus
    /// (the bridge is RabbitMQ-only — ADR-0001). Pure and broker-free so it is unit-testable.
    /// </summary>
    /// <param name="broker">
    /// Resolved broker name, or null when called outside the configuration flow (test or
    /// custom host) — in which case the guard is a no-op since the broker is unknown.
    /// </param>
    internal static void GuardMassTransitInteropSupported(string? broker)
    {
        if (broker == BrokerNames.AzureServiceBus)
        {
            throw new InvalidOperationException(
                $"The Wolverine MassTransit-interop listener (ListenToMassTransitQueue) is " +
                $"RabbitMQ-only: it is built on RabbitMQ exchange semantics with no Azure " +
                $"Service Bus equivalent, and porting it to Azure Service Bus was deliberately " +
                $"descoped (ADR-0001). '{BrokerSelector.MessageBrokerKey}' is " +
                $"'{BrokerNames.AzureServiceBus}', so this service cannot receive " +
                $"MassTransit-published events through Wolverine. Supported alternative: run " +
                $"this service with 'MessagingFramework=MassTransit' on this broker. Wolverine " +
                $"on Azure Service Bus remains valid for publishing and native " +
                $"Wolverine-to-Wolverine flows.");
        }
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
