using BuildingBlocks.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Wolverine.EntityFrameworkCore;

namespace BuildingBlocks.Infrastructure.Wolverine;

internal sealed class WolverineCommitStrategy<TDbContext> : ICommitStrategy
    where TDbContext : DbContext
{
    private readonly IDbContextOutbox<TDbContext> _outbox;

    public WolverineCommitStrategy(IDbContextOutbox<TDbContext> outbox)
        => _outbox = outbox;

    public async Task CommitAsync(CancellationToken ct = default)
        => await _outbox.SaveChangesAndFlushMessagesAsync(ct);
}
