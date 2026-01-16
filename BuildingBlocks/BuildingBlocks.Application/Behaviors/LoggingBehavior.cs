using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Application.Behaviors
{
    public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
    {
        protected readonly ILogger Logger;

        public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
        {
            Logger = logger;
        }

        public virtual async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            var requestName = typeof(TRequest).Name;
            OnHandling(requestName, request);

            try
            {
                var response = await next();
                OnHandled(requestName, request);
                return response;
            }
            catch (Exception ex)
            {
                OnError(requestName, request, ex);
                throw;
            }
        }

        protected virtual void OnHandling(string requestName, TRequest request)
            => Logger.LogInformation("Handling {RequestName}", requestName);

        protected virtual void OnHandled(string requestName, TRequest request)
            => Logger.LogInformation("Handled {RequestName}", requestName);

        protected virtual void OnError(string requestName, TRequest request, Exception ex)
        {
            // ValidationException is expected (client input error), not a system error
            if (ex is ValidationException) return;

            Logger.LogError(ex, "Error handling {RequestName}", requestName);
        }
    }
}
