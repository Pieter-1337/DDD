using BuildingBlocks.Application.Messaging;

namespace IntegrationEvents.Scheduling;

/// <summary>
/// Integration event published when a patient is suspended.
/// This is the public contract for cross-bounded-context communication.
/// Other bounded contexts (e.g., Billing, MedicalRecords) consume this event.
/// </summary>
public record PatientSuspendedIntegrationEvent(
    Guid PatientId
) : IntegrationEventBase;
