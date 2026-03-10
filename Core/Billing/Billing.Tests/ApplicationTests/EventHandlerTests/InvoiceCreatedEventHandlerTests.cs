using BuildingBlocks.Application.Interfaces;
using IntegrationEvents.Billing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Billing.Application.Invoices.EventHandlers;
using Billing.Domain.Invoices.Events;
using Shouldly;

namespace Billing.Tests.ApplicationTests.EventHandlerTests;

[TestClass]
public class InvoiceCreatedEventHandlerTests
{
    private Mock<IUnitOfWork> _unitOfWorkMock = null!;
    private InvoiceCreatedEventHandler _handler = null!;

    [TestInitialize]
    public void Setup()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        var logger = NullLogger<InvoiceCreatedEventHandler>.Instance;
        _handler = new InvoiceCreatedEventHandler(logger, _unitOfWorkMock.Object);
    }

    [TestMethod]
    public async Task Handle_QueuesIntegrationEvent_WithCorrectData()
    {
        // Arrange
        var invoiceId = Guid.NewGuid();
        var billingProfileId = Guid.NewGuid();
        var domainEvent = new InvoiceCreatedEvent(invoiceId, billingProfileId, 150.00m, "Consultation fee");

        // Act
        await _handler.Handle(domainEvent, CancellationToken.None);

        // Assert
        _unitOfWorkMock.Verify(u => u.QueueIntegrationEvent(
            It.Is<InvoiceCreatedIntegrationEvent>(e =>
                e.InvoiceId == invoiceId &&
                e.BillingProfileId == billingProfileId &&
                e.Amount == 150.00m &&
                e.Description == "Consultation fee")),
            Times.Once);
    }

    [TestMethod]
    public async Task Handle_QueuesIntegrationEvent_WithAllFieldsMapped()
    {
        // Arrange
        var invoiceId = Guid.NewGuid();
        var billingProfileId = Guid.NewGuid();
        var domainEvent = new InvoiceCreatedEvent(invoiceId, billingProfileId, 275.50m, "Lab work and diagnostics");

        InvoiceCreatedIntegrationEvent? capturedEvent = null;
        _unitOfWorkMock
            .Setup(u => u.QueueIntegrationEvent(It.IsAny<InvoiceCreatedIntegrationEvent>()))
            .Callback<object>(e => capturedEvent = e as InvoiceCreatedIntegrationEvent);

        // Act
        await _handler.Handle(domainEvent, CancellationToken.None);

        // Assert
        capturedEvent.ShouldNotBeNull();
        capturedEvent!.InvoiceId.ShouldBe(invoiceId);
        capturedEvent.BillingProfileId.ShouldBe(billingProfileId);
        capturedEvent.Amount.ShouldBe(275.50m);
        capturedEvent.Description.ShouldBe("Lab work and diagnostics");
    }

    [TestMethod]
    public async Task Handle_QueuesExactlyOneIntegrationEvent()
    {
        // Arrange
        var domainEvent = new InvoiceCreatedEvent(Guid.NewGuid(), Guid.NewGuid(), 75.00m, "Follow-up");

        // Act
        await _handler.Handle(domainEvent, CancellationToken.None);

        // Assert
        _unitOfWorkMock.Verify(
            u => u.QueueIntegrationEvent(It.IsAny<InvoiceCreatedIntegrationEvent>()),
            Times.Once);
    }

    [TestMethod]
    public async Task Handle_CompletesSuccessfully()
    {
        // Arrange
        var domainEvent = new InvoiceCreatedEvent(Guid.NewGuid(), Guid.NewGuid(), 200.00m, "Surgery");

        // Act & Assert - should not throw
        await Should.NotThrowAsync(async () =>
            await _handler.Handle(domainEvent, CancellationToken.None));
    }
}
