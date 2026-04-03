using Billing.Application.BillingProfiles.Commands;
using IntegrationEvents.Scheduling;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Billing.Infrastructure.Consumers.Wolverine;

public class PatientCreatedIntegrationEventHandler
{
    public async Task Handle(
        PatientCreatedIntegrationEvent message,
        IMediator mediator,
        ILogger<PatientCreatedIntegrationEventHandler> logger,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Creating billing profile for patient {PatientId} ({FullName})",
            message.PatientId,
            $"{message.FirstName} {message.LastName}");

        var command = new CreateBillingProfileCommand(
            new CreateBillingProfileRequest
            {
                PatientId = message.PatientId,
                Email = message.Email,
                FullName = $"{message.FirstName} {message.LastName}"
            });

        await mediator.Send(command, cancellationToken);
    }
}
