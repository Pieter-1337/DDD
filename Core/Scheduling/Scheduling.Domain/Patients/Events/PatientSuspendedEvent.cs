using BuildingBlocks.Domain.Events;

namespace Scheduling.Domain.Patients.Events;

public record PatientSuspendedEvent(Guid PatientId) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}
