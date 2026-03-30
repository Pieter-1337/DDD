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
        services.AddMassTransit(x =>
        {
            // Allow host to register consumers from specific assemblies
            configureConsumers?.Invoke(x);

            // Configure EF Core Transactional Outbox
            x.AddEntityFrameworkOutbox<TDbContext>(o =>
            {
                o.UseSqlServer();
                o.UseBusOutbox();                                   // Intercepts all Publish() calls, not just consumer-scoped ones
                o.QueryDelay = TimeSpan.FromSeconds(5);             // Background delivery polling interval (default: 1 minute)
                o.QueryMessageLimit = 100;                          // Max messages to fetch per poll (default: 100)
            });

            x.UsingRabbitMq((context, cfg) =>
            {
                // Try Aspire connection string first, fall back to manual config
                var connectionString = configuration.GetConnectionString("messaging");

                if (!string.IsNullOrEmpty(connectionString))
                {
                    // Aspire provides: amqp://guest:guest@localhost:5672
                    cfg.Host(new Uri(connectionString));
                }
                else
                {
                    // Fallback for non-Aspire environments (CI, production)
                    var rabbitMqSettings = configuration.GetSection("RabbitMQ");
                    cfg.Host(
                        rabbitMqSettings["Host"] ?? "localhost",
                        rabbitMqSettings["VirtualHost"] ?? "/",
                        h =>
                        {
                            h.Username(rabbitMqSettings["Username"] ?? "guest");
                            h.Password(rabbitMqSettings["Password"] ?? "guest");
                        });
                }


                // Configure retry policy
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
            });
        });

        // Register IEventBus implementation
        services.AddScoped<IEventBus, MassTransitEventBus>();

        return services;
    }
}