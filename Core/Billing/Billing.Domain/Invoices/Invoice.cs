using Billing.Domain.Invoices.Events;
using BuildingBlocks.Domain;

namespace Billing.Domain.Invoices;

public class Invoice : Entity
{
    public Guid BillingProfileId { get; private set; }
    public decimal Amount { get; private set; }
    public string Description { get; private set; } = null!;
    public InvoiceStatus Status { get; private set; } = null!;
    public DateTime CreatedAt { get; private set; }
    public DateTime? PaidAt { get; private set; }

    private Invoice() { }

    public static Invoice Create(
        Guid billingProfileId,
        decimal amount,
        string description)
    {
        var invoice = new Invoice
        {
            Id = Guid.NewGuid(),
            BillingProfileId = billingProfileId,
            Amount = amount,
            Description = description,
            Status = InvoiceStatus.Draft,
            CreatedAt = DateTime.UtcNow,
        };

        invoice.AddDomainEvent(new InvoiceCreatedEvent(
            invoice.Id,
            billingProfileId,
            amount,
            description));

        return invoice;
    }

    public void MarkAsSent()
    {
        Status = InvoiceStatus.Sent;
    }

    public void MarkAsPaid()
    {
        Status = InvoiceStatus.Paid;
        PaidAt = DateTime.UtcNow;
    }

    public void Cancel()
    {
        Status = InvoiceStatus.Cancelled;
    }
}
