namespace BuildingBlocks.Application.Messaging;

/// <summary>
/// Canonical message-broker names. Defined once here so the literal strings
/// cannot drift between the orchestrator, the services, and the framework
/// extensions. These are the only accepted values for the per-service
/// <c>MessageBroker</c> configuration setting.
/// </summary>
public static class BrokerNames
{
    /// <summary>RabbitMQ (the local-dev default).</summary>
    public const string RabbitMq = "RabbitMq";

    /// <summary>Azure Service Bus.</summary>
    public const string AzureServiceBus = "AzureServiceBus";
}
