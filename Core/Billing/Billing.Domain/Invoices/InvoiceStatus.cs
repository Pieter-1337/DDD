using BuildingBlocks.Enumerations;

namespace Billing.Domain.Invoices;

public sealed class InvoiceStatus : SmartEnumBase<InvoiceStatus>
{
    public static readonly InvoiceStatus Draft = new(nameof(Draft), 1);
    public static readonly InvoiceStatus Sent = new(nameof(Sent), 2);
    public static readonly InvoiceStatus Paid = new(nameof(Paid), 3);
    public static readonly InvoiceStatus Cancelled = new(nameof(Cancelled), 4);

    private InvoiceStatus(string name, int value) : base(name, value) { }
}
