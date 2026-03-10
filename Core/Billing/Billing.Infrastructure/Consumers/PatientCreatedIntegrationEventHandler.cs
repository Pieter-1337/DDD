using Billing.Application.BillingProfiles.Commands;
using BuildingBlocks.Infrastructure.MassTransit;
using IntegrationEvents.Scheduling;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Billing.Infrastructure.Consumers
{
    public class PatientCreatedIntegrationEventHandler : IntegrationEventHandler<PatientCreatedIntegrationEvent>
    {
        private readonly IMediator _mediator;

        public PatientCreatedIntegrationEventHandler(IMediator mediator, ILogger logger) : base (logger)
        {
            _mediator = mediator;
        }
        protected override async Task HandleAsync(PatientCreatedIntegrationEvent message, CancellationToken cancellationToken)
        {
            Logger.LogInformation("Creating billing profile for patient {PatientId} ({FullName})", message.PatientId, $"{message.FirstName} {message.LastName}");

            var command = new CreateBillingProfileCommand(new CreateBillingProfileRequest { PatientId = message.PatientId, Email = message.Email, FullName = $"{message.FirstName} {message.LastName}" });
            var response = await _mediator.Send(command, cancellationToken);            
        }
    }
}
