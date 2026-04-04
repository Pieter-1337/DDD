using System.Reflection;
using BuildingBlocks.Application.Interfaces;
using BuildingBlocks.Application.Messaging;
using FluentValidation;
using JasperFx;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.ErrorHandling;
using Wolverine.RabbitMQ;
using Wolverine.SqlServer;

namespace BuildingBlocks.Infrastructure.Wolverine;

public static class WolverineExtensions
{
    public static IHostApplicationBuilder AddWolverineEventBus<TDbContext>(
        this IHostApplicationBuilder builder,
        string dbConnectionString,
        string schemaName,
        Action<WolverineOptions>? configureWolverine = null)
        where TDbContext : DbContext
    {
        builder.Services.AddScoped<IEventBus, WolverineDbContextEventBus<TDbContext>>();
        builder.Services.AddScoped<ICommitStrategy, WolverineCommitStrategy<TDbContext>>();
        builder.Services.AddScoped(typeof(IDbContextOutbox<TDbContext>), typeof(DbContextOutbox<TDbContext>));

        builder.UseWolverine(opts =>
        {
            // Try Aspire connection string first, fall back to manual config
            var messagingConnectionString = builder.Configuration.GetConnectionString("messaging");

            if (!string.IsNullOrEmpty(messagingConnectionString))
            {
                opts.UseRabbitMq(new Uri(messagingConnectionString))
                    .AutoProvision();
            }
            else
            {
                var rabbitMqSettings = builder.Configuration.GetSection("RabbitMQ");
                opts.UseRabbitMq(factory =>
                {
                    factory.HostName = rabbitMqSettings["Host"] ?? "localhost";
                    factory.VirtualHost = rabbitMqSettings["VirtualHost"] ?? "/";
                    factory.UserName = rabbitMqSettings["Username"] ?? "guest";
                    factory.Password = rabbitMqSettings["Password"] ?? "guest";
                }).AutoProvision();
            }

            // Only discover handlers where the first parameter implements IIntegrationEvent.
            // This prevents accidental handler registration for non-event types.
            opts.Discovery.CustomizeHandlerDiscovery(q =>
            {
                q.Excludes.WithCondition("Non-IIntegrationEvent handlers", t =>
                    !HasIntegrationEventHandlerMethod(t));
            });

            // Configure transactional outbox with SQL Server (per-BC schema)
            opts.PersistMessagesWithSqlServer(dbConnectionString, schemaName);
            opts.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;

            // Use EF Core transactions for atomic outbox
            opts.UseEntityFrameworkCoreTransactions();

            // Configure retry policy — matches MassTransit's retry intervals
            opts.OnException<ValidationException>().MoveToErrorQueue();
            opts.OnException<ArgumentException>().MoveToErrorQueue();
            opts.OnAnyException().RetryWithCooldown(
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(15),
                TimeSpan.FromSeconds(30));

            configureWolverine?.Invoke(opts);
        });

        return builder;
    }

    /// <summary>
    /// Listens to a MassTransit-published message by binding a queue to MassTransit's exchange
    /// and configuring MassTransit envelope deserialization.
    /// </summary>
    public static WolverineOptions ListenToMassTransitQueue<TMessage>(
        this WolverineOptions opts,
        string queueName)
    {
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

    private static bool HasIntegrationEventHandlerMethod(Type type)
    {
        return type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Any(m =>
                m.Name is "Handle" or "HandleAsync" &&
                m.GetParameters().FirstOrDefault()?.ParameterType
                    .IsAssignableTo(typeof(IIntegrationEvent)) == true);
    }
}
