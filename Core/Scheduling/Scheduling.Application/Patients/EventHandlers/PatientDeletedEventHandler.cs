using BuildingBlocks.Application.Interfaces;
using IntegrationEvents.Scheduling;
using MediatR;
using Microsoft.Extensions.Logging;
using Scheduling.Domain.Patients.Events;

namespace Scheduling.Application.Patients.EventHandlers;

internal class PatientDeletedEventHandler : INotificationHandler<PatientDeletedEvent>
{
    private readonly ILogger<PatientDeletedEventHandler> _logger;
    private readonly IUnitOfWork _unitOfWork;

    public PatientDeletedEventHandler(ILogger<PatientDeletedEventHandler> logger, IUnitOfWork unitOfWork)
    {
        _logger = logger;
        _unitOfWork = unitOfWork;
    }

    public Task Handle(PatientDeletedEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Patient deleted: {PatientId}",
            notification.PatientId);

        _unitOfWork.QueueIntegrationEvent(new PatientDeletedIntegrationEvent(notification.PatientId));

        return Task.CompletedTask;
    }
}
