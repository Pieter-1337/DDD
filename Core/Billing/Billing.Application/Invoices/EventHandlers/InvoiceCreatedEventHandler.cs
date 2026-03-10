using Billing.Domain.Invoices.Events;
using BuildingBlocks.Application.Interfaces;
using IntegrationEvents.Billing;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Billing.Application.Invoices.EventHandlers;

internal class InvoiceCreatedEventHandler : INotificationHandler<InvoiceCreatedEvent>
{
    private readonly ILogger<InvoiceCreatedEventHandler> _logger;
    private readonly IUnitOfWork _unitOfWork;

    public InvoiceCreatedEventHandler(ILogger<InvoiceCreatedEventHandler> logger, IUnitOfWork unitOfWork)
    {
        _logger = logger;
        _unitOfWork = unitOfWork;
    }

    public Task Handle(InvoiceCreatedEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Invoice created: {InvoiceId} for billing profile {BillingProfileId}, amount {Amount}",
            notification.InvoiceId, notification.BillingProfileId, notification.Amount);

        _unitOfWork.QueueIntegrationEvent(new InvoiceCreatedIntegrationEvent(
            notification.InvoiceId,
            notification.BillingProfileId,
            notification.Amount,
            notification.Description));

        return Task.CompletedTask;
    }
}
