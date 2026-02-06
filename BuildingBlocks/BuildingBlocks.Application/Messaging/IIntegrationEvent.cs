namespace BuildingBlocks.Application.Messaging;

/// <summary>
/// Marker interface for integration events that cross bounded context boundaries.
/// </summary>
public interface IIntegrationEvent
{
    Guid EventId { get; }
    DateTime OccurredAt { get; }
    string? CorrelationId { get; }
}
