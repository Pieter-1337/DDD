using BuildingBlocks.Application.Interfaces;
using IntegrationEvents.Scheduling;
using MediatR;
using Microsoft.Extensions.Logging;
using Scheduling.Domain.Patients.Events;

namespace Scheduling.Application.Patients.EventHandlers;

/// <summary>
/// Handles the PatientCreatedEvent domain event.
/// Logs the event and queues an integration event for cross-bounded-context communication.
/// </summary>
internal class PatientCreatedEventHandler : INotificationHandler<PatientCreatedEvent>
{
    private readonly ILogger<PatientCreatedEventHandler> _logger;
    private readonly IUnitOfWork _unitOfWork;

    public PatientCreatedEventHandler(ILogger<PatientCreatedEventHandler> logger, IUnitOfWork unitOfWork)
    {
        _logger = logger;
        _unitOfWork = unitOfWork;
    }

    public Task Handle(PatientCreatedEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Patient created: {PatientId} - {FirstName} {LastName}",
            notification.PatientId, notification.FirstName, notification.LastName);

        // Queue integration event for cross-BC communication
        _unitOfWork.QueueIntegrationEvent(new PatientCreatedIntegrationEvent(
            notification.PatientId,
            notification.FirstName,
            notification.LastName,
            notification.Email,
            notification.DateOfBirth));

        return Task.CompletedTask;
    }
}
