namespace BuildingBlocks.Application.Messaging
{
    /// <summary>
    /// Base record for orchestration commands. These coordinate multiple child commands
    /// but do NOT get wrapped in a transaction themselves.
    ///
    /// Each child command dispatched via IMediator.Send() gets its own transaction.
    ///
    /// Use this for:
    /// - Long-running workflows
    /// - Saga/Process Manager patterns
    /// - Operations involving external services
    /// - When you need independent transaction boundaries per step
    ///
    /// IMPORTANT: Orchestration handlers should ONLY dispatch other commands,
    /// never perform direct database operations.
    /// </summary>
    /// <typeparam name="TResponse">The response type.</typeparam>
    public abstract record OrchestrationCommand<TResponse> : Command<TResponse>
    {
        /// <summary>
        /// Always true for orchestration commands - they never get wrapped in a transaction.
        /// </summary>
        public new bool SkipTransaction => true;
    }
}
