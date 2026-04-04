namespace BuildingBlocks.Application.Interfaces;

public interface ICommitStrategy
{
    Task CommitAsync(CancellationToken ct = default);
}
