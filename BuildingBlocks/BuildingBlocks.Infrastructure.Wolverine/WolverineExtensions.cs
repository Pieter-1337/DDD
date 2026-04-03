using System.Reflection;
using BuildingBlocks.Application.Messaging;
using FluentValidation;
using JasperFx;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;
using Wolverine;
using Wolverine.ErrorHandling;
using Wolverine.RabbitMQ;
using Wolverine.SqlServer;

namespace BuildingBlocks.Infrastructure.Wolverine;

public static class WolverineExtensions
{
    public static IHostApplicationBuilder AddWolverineEventBus(
        this IHostApplicationBuilder builder,
        string dbConnectionString,
        Action<WolverineOptions>? configureWolverine = null)
    {
        builder.Services.AddScoped<IEventBus, WolverineEventBus>();

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

            // Configure transactional outbox with SQL Server
            opts.PersistMessagesWithSqlServer(dbConnectionString, "wolverine");
            opts.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;

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

    private static bool HasIntegrationEventHandlerMethod(Type type)
    {
        return type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Any(m =>
                m.Name is "Handle" or "HandleAsync" &&
                m.GetParameters().FirstOrDefault()?.ParameterType
                    .IsAssignableTo(typeof(IIntegrationEvent)) == true);
    }
}
