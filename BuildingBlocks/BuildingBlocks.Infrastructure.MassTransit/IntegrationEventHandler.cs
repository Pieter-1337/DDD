using BuildingBlocks.Application.Messaging;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Infrastructure.MassTransit;

/// <summary>
/// Base class for integration event handlers that provides automatic logging.
/// Logs when handling starts, completes, and on errors.
/// </summary>
/// <typeparam name="TEvent">The integration event type to handle.</typeparam>
public abstract class IntegrationEventHandler<TEvent> : IConsumer<TEvent>
    where TEvent : class, IIntegrationEvent
{
    protected readonly ILogger Logger;

    protected IntegrationEventHandler(ILogger logger)
    {
        Logger = logger;
    }

    public async Task Consume(ConsumeContext<TEvent> context)
    {
        var eventType = typeof(TEvent).Name;
        var eventId = context.Message.EventId;

        Logger.LogInformation(
            "Handling {EventType} with EventId {EventId}",
            eventType,
            eventId);

        try
        {
            await HandleAsync(context.Message, context.CancellationToken);

            Logger.LogInformation(
                "Handled {EventType} with EventId {EventId}",
                eventType,
                eventId);
        }
        catch (Exception ex)
        {
            Logger.LogError(
                ex,
                "Error handling {EventType} with EventId {EventId}",
                eventType,
                eventId);
            throw;
        }
    }

    /// <summary>
    /// Implement this method to handle the integration event.
    /// Logging is handled automatically by the base class.
    /// </summary>
    protected abstract Task HandleAsync(TEvent message, CancellationToken cancellationToken);
}
