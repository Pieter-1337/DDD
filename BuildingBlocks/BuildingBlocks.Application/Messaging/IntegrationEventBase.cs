namespace BuildingBlocks.Application.Messaging;

/// <summary>
/// Base class for all integration events.
/// Integration events cross bounded context boundaries via message broker.
/// </summary>
public abstract record IntegrationEventBase : IIntegrationEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
    public string? CorrelationId { get; init; }
}
