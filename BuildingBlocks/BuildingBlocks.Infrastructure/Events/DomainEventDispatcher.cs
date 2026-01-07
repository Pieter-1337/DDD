using BuildingBlocks.Domain.Events;
using BuildingBlocks.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Infrastructure.Events;

public class DomainEventDispatcher : IDomainEventDispatcher
{
    private readonly IMediator _mediator;

    public DomainEventDispatcher(IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task DispatchEventsAsync(DbContext context, CancellationToken ct = default)
    {
        // Get all entities with domain events
        var entitiesWithEvents = context.ChangeTracker
            .Entries<IEntityBase>()
            .Where(e => e.Entity is IHasDomainEvents entityWithEvents && entityWithEvents.DomainEvents.Any())
            .Select(e => (IHasDomainEvents)e.Entity)
            .ToList();

        // Collect all events
        var domainEvents = entitiesWithEvents
            .SelectMany(e => e.DomainEvents)
            .ToList();

        // Clear events from entities (prevent re-dispatching)
        foreach (var entity in entitiesWithEvents)
        {
            entity.ClearDomainEvents();
        }

        // Dispatch each event
        foreach (var domainEvent in domainEvents)
        {
            await _mediator.Publish(domainEvent, ct);
        }
    }
}
