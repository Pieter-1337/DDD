using MediatR;
using System.Text.Json.Serialization;

namespace BuildingBlocks.Application.Cqrs
{
    /// <summary>
    /// Base record for commands. Commands are wrapped in a database transaction by default.
    /// </summary>
    /// <typeparam name="TResponse">The response type.</typeparam>
    public abstract record Command<TResponse> : IRequest<TResponse>
    {
        /// <summary>
        /// If true, the command will NOT be wrapped in a transaction.
        /// Used by OrchestrationCommand or when you need manual transaction control.
        /// </summary>
        [JsonIgnore]
        public bool SkipTransaction { get; init; }
    }
}
