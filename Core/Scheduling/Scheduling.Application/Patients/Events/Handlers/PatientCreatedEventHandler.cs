using MediatR;
using Microsoft.Extensions.Logging;
using Scheduling.Application.Patients.Events;

namespace Scheduling.Application.Patients.Events.Handlers
{
    public class PatientCreatedEventHandler : INotificationHandler<PatientCreatedEvent>
    {
        private readonly ILogger<PatientCreatedEventHandler> _logger;

        public PatientCreatedEventHandler(ILogger<PatientCreatedEventHandler> logger)
        {
            _logger = logger;
        }

        public Task Handle(PatientCreatedEvent notification, CancellationToken cancellationToken)
        {
            _logger.LogInformation(
            "Patient created: {PatientId} - {FirstName} {LastName} ({Email})",
            notification.PatientId,
            notification.FirstName,
            notification.LastName,
            notification.Email);

            // In real app: send welcome email, notify admin, etc.

            return Task.CompletedTask;
        }
    }
}
