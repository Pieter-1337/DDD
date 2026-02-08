using BuildingBlocks.Domain.Events;

namespace Scheduling.Domain.Patients.Events;

public record PatientCreatedEvent(
    Guid PatientId,
    string FirstName,
    string LastName,
    string Email,
    DateTime DateOfBirth) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}
