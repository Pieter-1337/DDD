using BuildingBlocks.Application.Messaging;
using FluentValidation;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Infrastructure.MassTransit.Configuration;

public static class MassTransitExtensions
{
    public static IServiceCollection AddMassTransitEventBus(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IRegistrationConfigurator>? configureConsumers = null)
    {
        services.AddMassTransit(x =>
        {
            // Allow host to register consumers from specific assemblies
            configureConsumers?.Invoke(x);

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