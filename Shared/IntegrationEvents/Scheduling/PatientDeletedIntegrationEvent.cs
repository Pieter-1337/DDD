using BuildingBlocks.Application.Messaging;

namespace IntegrationEvents.Scheduling;

public record PatientDeletedIntegrationEvent(
    Guid PatientId
) : IntegrationEventBase;
