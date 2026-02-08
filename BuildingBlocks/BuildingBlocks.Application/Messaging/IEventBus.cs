namespace BuildingBlocks.Application.Messaging;

/// <summary>
/// Abstraction for publishing integration events to a message broker.
/// Integration events are used for cross-bounded-context communication.
/// </summary>
public interface IEventBus
{
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
        where TEvent : class, IIntegrationEvent;
}
