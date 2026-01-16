using MediatR;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Application.Behaviors
{
    public class UnhandledExceptionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
    {
        protected readonly ILogger Logger;

        public UnhandledExceptionBehavior(ILogger<UnhandledExceptionBehavior<TRequest, TResponse>> logger)
        {
            Logger = logger;
        }
        public virtual async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            try
            {
                return await next();
            }
            catch (Exception ex)
            {
                var requestName = typeof(TRequest).Name;
                OnException(requestName, request, ex);
                throw;
            }
        }

        protected virtual void OnException(string requestName, TRequest request, Exception ex)
            => Logger.LogError(ex,
                "Unhandled exception for request {RequestName}: {@Request}",
                requestName,
                request);
    }
}
