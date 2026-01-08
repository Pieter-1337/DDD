using BuildingBlocks.Domain.Events;

namespace Scheduling.Application.Patients.Events;

public record PatientSuspendedEvent(Guid PatientId) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}
