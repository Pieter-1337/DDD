using BuildingBlocks.Domain.Events;

namespace Scheduling.Domain.Patients.Events;

public record PatientSuspendedEvent(
    Guid PatientId,
    string Reason) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}
