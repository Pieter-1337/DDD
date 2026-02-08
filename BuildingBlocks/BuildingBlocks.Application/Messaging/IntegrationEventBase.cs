namespace BuildingBlocks.Application.Messaging;

/// <summary>
/// Base class for integration events providing common properties.
/// </summary>
public abstract record IntegrationEventBase : IIntegrationEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}
