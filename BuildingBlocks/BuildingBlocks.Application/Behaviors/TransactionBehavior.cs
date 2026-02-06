using BuildingBlocks.Application.Interfaces;
using BuildingBlocks.Application.Cqrs;
using FluentValidation;
using MediatR;

namespace BuildingBlocks.Application.Behaviors
{
    /// <summary>
    /// Wraps command handlers in a database transaction.
    /// Queries and OrchestrationCommands are NOT wrapped.
    /// </summary>
    public class TransactionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : notnull
    {
        protected readonly IUnitOfWork UnitOfWork;

        public TransactionBehavior(IUnitOfWork unitOfWork)
        {
            UnitOfWork = unitOfWork;
        }

        public virtual async Task<TResponse> Handle(
            TRequest request,
            RequestHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken)
        {
            // Only wrap commands in transactions (not queries, not orchestration commands)
            if (!ShouldApplyTransaction(request))
            {
                return await next();
            }

            await UnitOfWork.BeginTransactionAsync(cancellationToken);

            try
            {
                var response = await next();
                await UnitOfWork.CloseTransactionAsync(cancellationToken: cancellationToken);
                return response;
            }
            catch (ValidationException)
            {
                // ValidationException is expected - commit any validator side effects
                // (usually none, but validators may have logged/audited)
                await UnitOfWork.CloseTransactionAsync(cancellationToken: cancellationToken);
                throw;
            }
            catch (Exception ex)
            {
                // Unexpected exception - rollback
                await UnitOfWork.CloseTransactionAsync(ex, cancellationToken);
                throw;
            }
        }

        /// <summary>
        /// Determines if this request should be wrapped in a transaction.
        /// Override to customize transaction behavior.
        /// </summary>
        protected virtual bool ShouldApplyTransaction(TRequest request)
        {
            // Check if it's a Command with transaction enabled
            if (request is Command<TResponse> command)
            {
                return !command.SkipTransaction;
            }

            // Queries and other request types don't get transactions
            return false;
        }
    }
}
