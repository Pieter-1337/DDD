using System.Reflection;
using BuildingBlocks.Application.Interfaces;
using BuildingBlocks.Application.Messaging;
using FluentValidation;
using JasperFx;
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
    /// <summary>
    /// Carries the resolved broker name from <see cref="AddWolverineEventBus{TDbContext}"/>
    /// into the <see cref="ListenToMassTransitQueue{TMessage}"/> interop helper, which the
    /// host invokes from inside the <c>configureWolverine</c> callback and therefore has no
    /// other way to learn the broker. <see cref="WolverineOptions"/> (the only object shared
    /// between the two) does not expose configuration at this package version, so the broker
    /// is flowed through an ambient slot instead. Wolverine configuration runs synchronously
    /// on a single thread during host build, so the value set inside the <c>UseWolverine</c>
    /// lambda is the value the helper reads. Kept private and reset in a <c>finally</c>.
    /// </summary>
    private static readonly AsyncLocal<string?> CurrentBroker = new();

    public static IHostApplicationBuilder AddWolverineEventBus<TDbContext>(
        this IHostApplicationBuilder builder,
        string dbConnectionString,
        string schemaName,
        Action<WolverineOptions>? configureWolverine = null)
        where TDbContext : DbContext
    {
        // Resolve and validate the broker decision through the single
        // broker-selection module (BuildingBlocks.Application.Messaging). It owns
        // reading 'MessageBroker' (defaulting to RabbitMq), resolving the single
        // 'messaging' connection string, and the fail-fast format guards — in
        // exactly one place. We never duplicate broker constants, config reading,
        // or connection-string validation here.
        //
        // Behaviour change vs. the previous code: the old 'RabbitMQ:' config-section
        // fallback is gone. The module now requires the single 'messaging' connection
        // string for every broker, so ConnectionStrings:messaging is required in
        // non-Aspire environments too (this mirrors the MassTransit extension's #3
        // change — see ADR-0001 and the PR body).
        var selection = BrokerSelector.Resolve(builder.Configuration);

        builder.Services.AddScoped<IEventBus, WolverineDbContextEventBus<TDbContext>>();
        builder.Services.AddScoped<ICommitStrategy, WolverineCommitStrategy<TDbContext>>();
        builder.Services.AddScoped(typeof(IDbContextOutbox<TDbContext>), typeof(DbContextOutbox<TDbContext>));

        builder.UseWolverine(opts =>
        {
            // --- Transport branch (the only broker-specific code) ---
            if (selection.Broker == BrokerNames.AzureServiceBus)
            {
                // Single connection string, symmetric with the RabbitMQ branch and
                // correct for real Azure namespaces (one endpoint serves both the
                // AMQP data plane and the HTTP management plane). On the emulator,
                // the 'UseDevelopmentEmulator=true' flag inside the 'messaging'
                // connection string drives the Azure SDK's emulator handling.
                //
                // NOTE (vs. MassTransit): at this WolverineFx version (4.12.2) the
                // Azure Service Bus transport derives both its ServiceBusClient and
                // ServiceBusAdministrationClient from the single connection string —
                // it exposes no overload to inject a custom administration client or
                // a separate admin connection string. So Wolverine does NOT consume
                // the AppHost's emulator-only 'messaging-admin' string the way the
                // MassTransit extension does; one code path covers both emulator and
                // real namespace here. See ADR-0001 and the PR body.
                opts.UseAzureServiceBus(selection.ConnectionString)
                    .AutoProvision();
            }
            else
            {
                // The broker-selection module already validated this is an AMQP URI
                // (e.g. Aspire's amqp://guest:guest@localhost:5672).
                opts.UseRabbitMq(new Uri(selection.ConnectionString))
                    .AutoProvision();
            }

            // --- Broker-agnostic configuration (shared above the transport branch) ---

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

            // Configure retry policy — matches MassTransit's retry intervals. These
            // policies are global on 'opts' (transport-agnostic), so the intervals
            // are identical across both brokers and cannot drift.
            opts.OnException<ValidationException>().MoveToErrorQueue();
            opts.OnException<ArgumentException>().MoveToErrorQueue();
            opts.OnAnyException().RetryWithCooldown(
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(15),
                TimeSpan.FromSeconds(30));

            // Flow the resolved broker into the interop helper for the duration of the
            // host's configuration callback. The 'configureWolverine' callback is where
            // the host calls ListenToMassTransitQueue<T>, which must fail fast on Azure
            // Service Bus. Set inside this (deferred) UseWolverine lambda — not in the
            // method body — because the lambda runs at host-build time, after the body.
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
    /// This interop bridge is RabbitMQ-only: it is built on RabbitMQ exchange semantics
    /// (<c>BindExchange</c>/<c>ToQueue</c> + MassTransit's <c>Namespace:TypeName</c> exchange
    /// naming), which have no Azure Service Bus equivalent. Porting it to Azure Service Bus
    /// topics/subscriptions was deliberately descoped (ADR-0001), so it fails fast at startup
    /// on Azure Service Bus rather than silently receiving nothing.
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
    /// Fails fast when the MassTransit-interop listener is configured on Azure Service Bus.
    /// The bridge is RabbitMQ-only (ADR-0001); on Azure Service Bus the supported alternative
    /// is to run this service with <c>MessagingFramework=MassTransit</c>. Pure and broker-free
    /// so it is unit-testable without a running broker.
    /// </summary>
    /// <param name="broker">
    /// The resolved broker name (one of <see cref="BrokerNames"/>). A null value means the
    /// helper was called outside <see cref="AddWolverineEventBus{TDbContext}"/>'s configuration
    /// flow (e.g. in a test or a custom host); in that case the guard is a no-op since the broker
    /// is unknown — RabbitMQ wiring then surfaces any real misconfiguration on its own.
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
