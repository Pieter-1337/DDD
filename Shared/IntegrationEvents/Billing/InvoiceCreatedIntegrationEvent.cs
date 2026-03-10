using BuildingBlocks.Application.Messaging;

namespace IntegrationEvents.Billing;

public record InvoiceCreatedIntegrationEvent(
    Guid InvoiceId,
    Guid BillingProfileId,
    decimal Amount,
    string Description) : IntegrationEventBase;
