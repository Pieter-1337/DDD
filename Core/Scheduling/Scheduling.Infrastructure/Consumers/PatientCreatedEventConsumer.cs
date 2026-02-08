using IntegrationEvents.Scheduling;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Scheduling.Infrastructure.Consumers;

/// <summary>
/// MassTransit consumer for PatientCreatedIntegrationEvent.
/// Handles cross-bounded-context processing when a new patient is created.
/// </summary>
public class PatientCreatedEventConsumer : IConsumer<PatientCreatedIntegrationEvent>
{
    private readonly ILogger<PatientCreatedEventConsumer> _logger;

    public PatientCreatedEventConsumer(ILogger<PatientCreatedEventConsumer> logger)
    {
        _logger = logger;
    }

    public Task Consume(ConsumeContext<PatientCreatedIntegrationEvent> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "Consumed PatientCreatedIntegrationEvent: {PatientId} - {FirstName} {LastName} ({Email})",
            message.PatientId,
            message.FirstName,
            message.LastName,
            message.Email);

        // TODO: Add cross-bounded-context logic here
        // For example: notify Billing or MedicalRecords bounded contexts

        return Task.CompletedTask;
    }
}
