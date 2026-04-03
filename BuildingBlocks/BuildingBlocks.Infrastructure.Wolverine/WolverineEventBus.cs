using BuildingBlocks.Application.Messaging;
using Wolverine;

namespace BuildingBlocks.Infrastructure.Wolverine;

internal sealed class WolverineEventBus : IEventBus
{
    private readonly IMessageBus _messageBus;

    public WolverineEventBus(IMessageBus messageBus)
    {
        _messageBus = messageBus;
    }

    public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
        where TEvent : class, IIntegrationEvent
    {
        await _messageBus.PublishAsync(@event);
    }
}
