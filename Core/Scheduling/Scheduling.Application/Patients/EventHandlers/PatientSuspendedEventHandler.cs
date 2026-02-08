using BuildingBlocks.Application.Interfaces;
using IntegrationEvents.Scheduling;
using MediatR;
using Microsoft.Extensions.Logging;
using Scheduling.Domain.Patients.Events;

namespace Scheduling.Application.Patients.EventHandlers;

/// <summary>
/// Handles the PatientSuspendedEvent domain event.
/// Logs the event and queues an integration event for cross-bounded-context communication.
/// </summary>
internal class PatientSuspendedEventHandler : INotificationHandler<PatientSuspendedEvent>
{
    private readonly ILogger<PatientSuspendedEventHandler> _logger;
    private readonly IUnitOfWork _unitOfWork;

    public PatientSuspendedEventHandler(ILogger<PatientSuspendedEventHandler> logger, IUnitOfWork unitOfWork)
    {
        _logger = logger;
        _unitOfWork = unitOfWork;
    }

    public Task Handle(PatientSuspendedEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Patient suspended: {PatientId} - Reason: {Reason}",
            notification.PatientId, notification.Reason);

        // Queue integration event for cross-BC communication
        _unitOfWork.QueueIntegrationEvent(new PatientSuspendedIntegrationEvent(notification.PatientId));

        return Task.CompletedTask;
    }
}
