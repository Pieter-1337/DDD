using Billing.Domain.Invoices;
using Billing.Domain.Invoices.Events;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace Billing.Tests.DomainTests.Invoices;

[TestClass]
public class InvoiceTests
{
    [TestMethod]
    public void Create_ShouldCreateInvoiceWithCorrectValues()
    {
        // Arrange
        var billingProfileId = Guid.NewGuid();

        // Act
        var invoice = Invoice.Create(billingProfileId, 150.00m, "Consultation fee");

        // Assert
        invoice.Id.ShouldNotBe(Guid.Empty);
        invoice.BillingProfileId.ShouldBe(billingProfileId);
        invoice.Amount.ShouldBe(150.00m);
        invoice.Description.ShouldBe("Consultation fee");
        invoice.Status.ShouldBe(InvoiceStatus.Draft);
        invoice.CreatedAt.ShouldNotBe(default);
        invoice.PaidAt.ShouldBeNull();
    }

    [TestMethod]
    public void Create_ShouldRaiseInvoiceCreatedEvent()
    {
        // Arrange
        var billingProfileId = Guid.NewGuid();

        // Act
        var invoice = Invoice.Create(billingProfileId, 150.00m, "Consultation fee");

        // Assert
        invoice.DomainEvents.Count.ShouldBe(1);
        var domainEvent = invoice.DomainEvents[0].ShouldBeOfType<InvoiceCreatedEvent>();
        domainEvent.InvoiceId.ShouldBe(invoice.Id);
        domainEvent.BillingProfileId.ShouldBe(billingProfileId);
        domainEvent.Amount.ShouldBe(150.00m);
        domainEvent.Description.ShouldBe("Consultation fee");
    }

    [TestMethod]
    public void MarkAsSent_ShouldChangeStatusToSent()
    {
        // Arrange
        var invoice = Invoice.Create(Guid.NewGuid(), 100.00m, "Test");

        // Act
        invoice.MarkAsSent();

        // Assert
        invoice.Status.ShouldBe(InvoiceStatus.Sent);
    }

    [TestMethod]
    public void MarkAsPaid_ShouldChangeStatusAndSetPaidAt()
    {
        // Arrange
        var invoice = Invoice.Create(Guid.NewGuid(), 100.00m, "Test");

        // Act
        invoice.MarkAsPaid();

        // Assert
        invoice.Status.ShouldBe(InvoiceStatus.Paid);
        invoice.PaidAt.ShouldNotBeNull();
    }

    [TestMethod]
    public void Cancel_ShouldChangeStatusToCancelled()
    {
        // Arrange
        var invoice = Invoice.Create(Guid.NewGuid(), 100.00m, "Test");

        // Act
        invoice.Cancel();

        // Assert
        invoice.Status.ShouldBe(InvoiceStatus.Cancelled);
    }
}
