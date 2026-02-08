using BuildingBlocks.Application.Messaging;

namespace Shared.IntegrationEvents.Scheduling;

/// <summary>
/// Published when a new patient is created in the Scheduling bounded context.
/// Consumed by Billing and MedicalRecords bounded contexts.
/// </summary>
public record PatientCreatedIntegrationEvent : IntegrationEventBase
{
    public Guid PatientId { get; init; }
    public string Email { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public DateTime DateOfBirth { get; init; }
}