using MediatR;

namespace BuildingBlocks.Application.Cqrs
{
    /// <summary>
    /// Base record for queries. Queries are NOT wrapped in a database transaction.
    /// Queries should be read-only operations.
    /// </summary>
    /// <typeparam name="TResponse">The response type.</typeparam>
    public abstract record Query<TResponse> : IRequest<TResponse>;
}
