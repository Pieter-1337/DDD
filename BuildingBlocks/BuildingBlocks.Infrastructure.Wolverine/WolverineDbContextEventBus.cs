using BuildingBlocks.Application.Messaging;
using Microsoft.EntityFrameworkCore;
using Wolverine.EntityFrameworkCore;

namespace BuildingBlocks.Infrastructure.Wolverine;

internal sealed class WolverineDbContextEventBus<TDbContext> : IEventBus
    where TDbContext : DbContext
{
    private readonly IDbContextOutbox<TDbContext> _outbox;

    public WolverineDbContextEventBus(IDbContextOutbox<TDbContext> outbox)
        => _outbox = outbox;

    public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
        where TEvent : class, IIntegrationEvent
        => await _outbox.PublishAsync(@event);
}
