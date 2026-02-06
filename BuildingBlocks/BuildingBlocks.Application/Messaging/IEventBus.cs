namespace BuildingBlocks.Application.Messaging;

/// <summary>
/// Abstraction for publishing integration events to a message broker.
/// </summary>
public interface IEventBus
{
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
        where TEvent : class, IIntegrationEvent;
}
