using BuildingBlocks.Domain.Events;

namespace Billing.Domain.BillingProfiles.Events;

public record BillingProfileCreatedEvent(
    Guid BillingProfileId,
    Guid PatientId,
    string Email,
    string FullName) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}
