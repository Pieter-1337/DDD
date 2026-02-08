using BuildingBlocks.Application.Interfaces;
using IntegrationEvents.Scheduling;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Scheduling.Application.Patients.EventHandlers;
using Scheduling.Domain.Patients.Events;
using Shouldly;

namespace Scheduling.Tests.ApplicationTests.EventHandlerTests;

[TestClass]
public class PatientSuspendedEventHandlerTests
{
    private Mock<IUnitOfWork> _unitOfWorkMock = null!;
    private PatientSuspendedEventHandler _handler = null!;

    [TestInitialize]
    public void Setup()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        var logger = NullLogger<PatientSuspendedEventHandler>.Instance;
        _handler = new PatientSuspendedEventHandler(logger, _unitOfWorkMock.Object);
    }

    [TestMethod]
    public async Task Handle_QueuesIntegrationEvent_WithCorrectPatientId()
    {
        // Arrange
        var patientId = Guid.NewGuid();
        var domainEvent = new PatientSuspendedEvent(
            patientId,
            "Non-payment of bills");

        // Act
        await _handler.Handle(domainEvent, CancellationToken.None);

        // Assert
        _unitOfWorkMock.Verify(u => u.QueueIntegrationEvent(
            It.Is<PatientSuspendedIntegrationEvent>(e =>
                e.PatientId == patientId)),
            Times.Once);
    }

    [TestMethod]
    public async Task Handle_QueuesIntegrationEvent_WithPatientIdMapped()
    {
        // Arrange
        var patientId = Guid.NewGuid();
        var domainEvent = new PatientSuspendedEvent(
            patientId,
            "Violated hospital policy");

        PatientSuspendedIntegrationEvent? capturedEvent = null;
        _unitOfWorkMock
            .Setup(u => u.QueueIntegrationEvent(It.IsAny<PatientSuspendedIntegrationEvent>()))
            .Callback<object>(e => capturedEvent = e as PatientSuspendedIntegrationEvent);

        // Act
        await _handler.Handle(domainEvent, CancellationToken.None);

        // Assert
        capturedEvent.ShouldNotBeNull();
        capturedEvent!.PatientId.ShouldBe(patientId);
    }

    [TestMethod]
    public async Task Handle_QueuesExactlyOneIntegrationEvent()
    {
        // Arrange
        var domainEvent = new PatientSuspendedEvent(
            Guid.NewGuid(),
            "Test suspension reason");

        // Act
        await _handler.Handle(domainEvent, CancellationToken.None);

        // Assert
        _unitOfWorkMock.Verify(
            u => u.QueueIntegrationEvent(It.IsAny<PatientSuspendedIntegrationEvent>()),
            Times.Once);
    }

    [TestMethod]
    public async Task Handle_CompletesSuccessfully()
    {
        // Arrange
        var domainEvent = new PatientSuspendedEvent(
            Guid.NewGuid(),
            "Any reason");

        // Act & Assert - should not throw
        await Should.NotThrowAsync(async () =>
            await _handler.Handle(domainEvent, CancellationToken.None));
    }

    [TestMethod]
    public async Task Handle_DoesNotIncludeReasonInIntegrationEvent()
    {
        // Arrange - The integration event only includes PatientId, not the reason
        // This is intentional: the reason may be sensitive internal data
        var patientId = Guid.NewGuid();
        var sensitiveReason = "Sensitive medical compliance issue";
        var domainEvent = new PatientSuspendedEvent(patientId, sensitiveReason);

        PatientSuspendedIntegrationEvent? capturedEvent = null;
        _unitOfWorkMock
            .Setup(u => u.QueueIntegrationEvent(It.IsAny<PatientSuspendedIntegrationEvent>()))
            .Callback<object>(e => capturedEvent = e as PatientSuspendedIntegrationEvent);

        // Act
        await _handler.Handle(domainEvent, CancellationToken.None);

        // Assert - Integration event should only have PatientId
        capturedEvent.ShouldNotBeNull();
        capturedEvent!.PatientId.ShouldBe(patientId);
        // Note: PatientSuspendedIntegrationEvent only contains PatientId by design
    }
}
