using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Infrastructure.Events;

public interface IDomainEventDispatcher
{
    Task DispatchEventsAsync(DbContext context, CancellationToken ct = default);
}
