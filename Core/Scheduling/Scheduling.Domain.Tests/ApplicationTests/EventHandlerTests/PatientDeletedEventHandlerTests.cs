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
public class PatientDeletedEventHandlerTests
{
    private Mock<IUnitOfWork> _unitOfWorkMock = null!;
    private PatientDeletedEventHandler _handler = null!;

    [TestInitialize]
    public void Setup()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        var logger = NullLogger<PatientDeletedEventHandler>.Instance;
        _handler = new PatientDeletedEventHandler(logger, _unitOfWorkMock.Object);
    }

    [TestMethod]
    public async Task Handle_QueuesIntegrationEvent_WithCorrectPatientId()
    {
        // Arrange
        var patientId = Guid.NewGuid();
        var domainEvent = new PatientDeletedEvent(patientId);

        // Act
        await _handler.Handle(domainEvent, CancellationToken.None);

        // Assert
        _unitOfWorkMock.Verify(u => u.QueueIntegrationEvent(
            It.Is<PatientDeletedIntegrationEvent>(e =>
                e.PatientId == patientId)),
            Times.Once);
    }

    [TestMethod]
    public async Task Handle_QueuesIntegrationEvent_WithPatientIdMapped()
    {
        // Arrange
        var patientId = Guid.NewGuid();
        var domainEvent = new PatientDeletedEvent(patientId);

        PatientDeletedIntegrationEvent? capturedEvent = null;
        _unitOfWorkMock
            .Setup(u => u.QueueIntegrationEvent(It.IsAny<PatientDeletedIntegrationEvent>()))
            .Callback<object>(e => capturedEvent = e as PatientDeletedIntegrationEvent);

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
        var domainEvent = new PatientDeletedEvent(Guid.NewGuid());

        // Act
        await _handler.Handle(domainEvent, CancellationToken.None);

        // Assert
        _unitOfWorkMock.Verify(
            u => u.QueueIntegrationEvent(It.IsAny<PatientDeletedIntegrationEvent>()),
            Times.Once);
    }

    [TestMethod]
    public async Task Handle_CompletesSuccessfully()
    {
        // Arrange
        var domainEvent = new PatientDeletedEvent(Guid.NewGuid());

        // Act & Assert - should not throw
        await Should.NotThrowAsync(async () =>
            await _handler.Handle(domainEvent, CancellationToken.None));
    }
}
