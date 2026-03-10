using BuildingBlocks.Domain.Events;

namespace Billing.Domain.Invoices.Events;

public record InvoiceCreatedEvent(
    Guid InvoiceId,
    Guid BillingProfileId,
    decimal Amount,
    string Description) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}
