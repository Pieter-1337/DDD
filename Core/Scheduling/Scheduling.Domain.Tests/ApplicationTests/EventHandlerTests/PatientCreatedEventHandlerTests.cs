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
public class PatientCreatedEventHandlerTests
{
    private Mock<IUnitOfWork> _unitOfWorkMock = null!;
    private PatientCreatedEventHandler _handler = null!;

    [TestInitialize]
    public void Setup()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        var logger = NullLogger<PatientCreatedEventHandler>.Instance;
        _handler = new PatientCreatedEventHandler(logger, _unitOfWorkMock.Object);
    }

    [TestMethod]
    public async Task Handle_QueuesIntegrationEvent_WithCorrectData()
    {
        // Arrange
        var patientId = Guid.NewGuid();
        var domainEvent = new PatientCreatedEvent(
            patientId,
            "John",
            "Doe",
            "john@example.com",
            new DateTime(1990, 1, 1));

        // Act
        await _handler.Handle(domainEvent, CancellationToken.None);

        // Assert
        _unitOfWorkMock.Verify(u => u.QueueIntegrationEvent(
            It.Is<PatientCreatedIntegrationEvent>(e =>
                e.PatientId == patientId &&
                e.FirstName == "John" &&
                e.LastName == "Doe" &&
                e.Email == "john@example.com" &&
                e.DateOfBirth == new DateTime(1990, 1, 1))),
            Times.Once);
    }

    [TestMethod]
    public async Task Handle_QueuesIntegrationEvent_WithAllFieldsMapped()
    {
        // Arrange
        var patientId = Guid.NewGuid();
        var dateOfBirth = new DateTime(1985, 6, 15);
        var domainEvent = new PatientCreatedEvent(
            patientId,
            "Jane",
            "Smith",
            "jane.smith@hospital.com",
            dateOfBirth);

        PatientCreatedIntegrationEvent? capturedEvent = null;
        _unitOfWorkMock
            .Setup(u => u.QueueIntegrationEvent(It.IsAny<PatientCreatedIntegrationEvent>()))
            .Callback<object>(e => capturedEvent = e as PatientCreatedIntegrationEvent);

        // Act
        await _handler.Handle(domainEvent, CancellationToken.None);

        // Assert
        capturedEvent.ShouldNotBeNull();
        capturedEvent!.PatientId.ShouldBe(patientId);
        capturedEvent.FirstName.ShouldBe("Jane");
        capturedEvent.LastName.ShouldBe("Smith");
        capturedEvent.Email.ShouldBe("jane.smith@hospital.com");
        capturedEvent.DateOfBirth.ShouldBe(dateOfBirth);
    }

    [TestMethod]
    public async Task Handle_QueuesExactlyOneIntegrationEvent()
    {
        // Arrange
        var domainEvent = new PatientCreatedEvent(
            Guid.NewGuid(),
            "Test",
            "Patient",
            "test@example.com",
            DateTime.UtcNow.AddYears(-30));

        // Act
        await _handler.Handle(domainEvent, CancellationToken.None);

        // Assert
        _unitOfWorkMock.Verify(
            u => u.QueueIntegrationEvent(It.IsAny<PatientCreatedIntegrationEvent>()),
            Times.Once);
    }

    [TestMethod]
    public async Task Handle_CompletesSuccessfully()
    {
        // Arrange
        var domainEvent = new PatientCreatedEvent(
            Guid.NewGuid(),
            "Test",
            "Patient",
            "test@example.com",
            DateTime.UtcNow.AddYears(-25));

        // Act & Assert - should not throw
        await Should.NotThrowAsync(async () =>
            await _handler.Handle(domainEvent, CancellationToken.None));
    }
}
