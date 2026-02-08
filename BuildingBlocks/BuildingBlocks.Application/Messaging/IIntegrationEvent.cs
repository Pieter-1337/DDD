namespace BuildingBlocks.Application.Messaging;

/// <summary>
/// Marker interface for integration events.
/// Integration events are used for cross-bounded-context communication via a message broker.
/// Unlike domain events (which are internal to a bounded context), integration events
/// represent the public contract between services.
/// </summary>
public interface IIntegrationEvent
{
    /// <summary>
    /// Unique identifier for this event instance.
    /// </summary>
    Guid EventId { get; }

    /// <summary>
    /// When this event occurred.
    /// </summary>
    DateTime OccurredOn { get; }
}
