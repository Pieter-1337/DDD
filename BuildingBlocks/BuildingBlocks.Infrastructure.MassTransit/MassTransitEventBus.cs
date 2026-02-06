using BuildingBlocks.Application.Messaging;
using MassTransit;

namespace BuildingBlocks.Infrastructure.MassTransit;

public class MassTransitEventBus : IEventBus
{
    private readonly IPublishEndpoint _publishEndpoint;

    public MassTransitEventBus(IPublishEndpoint publishEndpoint)
    {
        _publishEndpoint = publishEndpoint;
    }

    public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
        where TEvent : class, IIntegrationEvent
    {
        await _publishEndpoint.Publish(@event, ct);
    }
}
