using BuildingBlocks.Application.Interfaces;
using IntegrationEvents.Scheduling;
using MediatR;
using Microsoft.Extensions.Logging;
using Scheduling.Domain.Patients.Events;

namespace Scheduling.Application.Patients.EventHandlers;

/// <summary>
/// Handles the PatientActivatedEvent domain event.
/// Logs the event and queues an integration event for cross-bounded-context communication.
/// </summary>
internal class PatientActivatedEventHandler : INotificationHandler<PatientActivatedEvent>
{
    private readonly ILogger<PatientActivatedEventHandler> _logger;
    private readonly IUnitOfWork _unitOfWork;

    public PatientActivatedEventHandler(ILogger<PatientActivatedEventHandler> logger, IUnitOfWork unitOfWork)
    {
        _logger = logger;
        _unitOfWork = unitOfWork;
    }

    public Task Handle(PatientActivatedEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Patient activated: {PatientId}",
            notification.PatientId);

        // Queue integration event for cross-BC communication
        _unitOfWork.QueueIntegrationEvent(new PatientActivatedIntegrationEvent(notification.PatientId));

        return Task.CompletedTask;
    }
}
