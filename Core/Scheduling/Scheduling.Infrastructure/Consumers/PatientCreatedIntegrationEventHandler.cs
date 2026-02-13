using BuildingBlocks.Infrastructure.MassTransit;
using IntegrationEvents.Scheduling;
using Microsoft.Extensions.Logging;

namespace Scheduling.Infrastructure.Consumers;

/// <summary>
/// Handler for PatientCreatedIntegrationEvent.
/// Handles cross-bounded-context processing when a new patient is created.
/// </summary>
public class PatientCreatedIntegrationEventHandler
    : IntegrationEventHandler<PatientCreatedIntegrationEvent>
{
    public PatientCreatedIntegrationEventHandler(
        ILogger<PatientCreatedIntegrationEventHandler> logger) : base(logger)
    {
    }

    protected override Task HandleAsync(
        PatientCreatedIntegrationEvent message,
        CancellationToken cancellationToken)
    {
        // Business logic only - logging is automatic via base class
        Logger.LogInformation(
            "Processing patient {PatientId} - {FirstName} {LastName} ({Email})",
            message.PatientId,
            message.FirstName,
            message.LastName,
            message.Email);

        // TODO: Add cross-bounded-context logic here
        // For example: notify Billing or MedicalRecords bounded contexts

        return Task.CompletedTask;
    }
}
