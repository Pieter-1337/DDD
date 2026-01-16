using MediatR;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace BuildingBlocks.Application.Behaviors
{
    public class PerformanceBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TRequest> where TRequest : notnull
    {
        protected readonly ILogger Logger;
        protected readonly Stopwatch Timer;

        public PerformanceBehavior(ILogger<PerformanceBehavior<TRequest, TResponse>> logger)
        {
            Logger = logger;
            Timer = new Stopwatch();
        }

        protected virtual int ThresholdMilliseconds => 500;
        public virtual async Task<TRequest> Handle(TRequest request, RequestHandlerDelegate<TRequest> next, CancellationToken cancellationToken)
        {
            Timer.Start();

            var response = await next();

            Timer.Stop();

            var elapsedMilliseconds = Timer.ElapsedMilliseconds;
            if (elapsedMilliseconds > ThresholdMilliseconds) 
            {
                var requestName = typeof(TRequest).Name;
                OnSlowRequest(requestName, request, elapsedMilliseconds);
            }

            return response;
        }

        protected virtual void OnSlowRequest(string requestName, TRequest request, long elapsedMilliseconds) 
            => Logger.LogWarning("Long running request: {RequestName} ({ElapsedMilliseconds})", requestName, elapsedMilliseconds);
    }
}
