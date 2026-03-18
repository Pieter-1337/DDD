using BuildingBlocks.Domain.Events;

namespace Scheduling.Domain.Patients.Events;

public record PatientActivatedEvent(
    Guid PatientId) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}
