using Microsoft.Extensions.Configuration;

namespace BuildingBlocks.Application.Messaging;

/// <summary>
/// The single deep module that owns the message-broker decision for the whole
/// system. Given a service's configuration it produces a validated
/// <see cref="BrokerSelection"/> (broker choice + <c>messaging</c> connection
/// string) or throws a descriptive startup exception that names the
/// misalignment and the fix.
///
/// This module owns:
/// <list type="bullet">
///   <item>reading the per-service <c>MessageBroker</c> value, defaulting to
///   <see cref="BrokerNames.RabbitMq"/> when unset (per ADR-0001);</item>
///   <item>resolving the single <c>messaging</c> connection string;</item>
///   <item>the fail-fast guard that the connection-string format matches the
///   configured broker (AMQP URI for RabbitMQ, Service Bus endpoint format for
///   Azure Service Bus).</item>
/// </list>
///
/// It has no dependency on any broker, container, or messaging framework; the
/// framework extensions consume it.
/// </summary>
public static class BrokerSelector
{
    /// <summary>The configuration key holding the per-service broker choice.</summary>
    public const string MessageBrokerKey = "MessageBroker";

    /// <summary>The single connection-string name reused for every broker.</summary>
    public const string MessagingConnectionStringName = "messaging";

    /// <summary>
    /// Resolves and validates the broker selection from configuration.
    /// </summary>
    /// <param name="configuration">The service's configuration.</param>
    /// <returns>The validated <see cref="BrokerSelection"/>.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="configuration"/> is null.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The <c>MessageBroker</c> value is unrecognized, the <c>messaging</c>
    /// connection string is missing, or the connection-string format does not
    /// match the configured broker.
    /// </exception>
    public static BrokerSelection Resolve(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var broker = ResolveBrokerName(configuration);
        var connectionString = ResolveConnectionString(configuration);
        GuardFormatMatchesBroker(broker, connectionString);

        return new BrokerSelection(broker, connectionString);
    }

    private static string ResolveBrokerName(IConfiguration configuration)
    {
        // Indexer (not GetValue<T>) so this module needs only
        // Microsoft.Extensions.Configuration.Abstractions.
        var configured = configuration[MessageBrokerKey];

        // Unset / blank => default to RabbitMq so existing environments keep
        // working with zero config changes (per ADR-0001).
        if (string.IsNullOrWhiteSpace(configured))
        {
            return BrokerNames.RabbitMq;
        }

        var trimmed = configured.Trim();

        if (string.Equals(trimmed, BrokerNames.RabbitMq, StringComparison.OrdinalIgnoreCase))
        {
            return BrokerNames.RabbitMq;
        }

        if (string.Equals(trimmed, BrokerNames.AzureServiceBus, StringComparison.OrdinalIgnoreCase))
        {
            return BrokerNames.AzureServiceBus;
        }

        throw new InvalidOperationException(
            $"Unrecognized '{MessageBrokerKey}' value '{configured}'. " +
            $"Valid values are '{BrokerNames.RabbitMq}' and '{BrokerNames.AzureServiceBus}'.");
    }

    private static string ResolveConnectionString(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(MessagingConnectionStringName);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"The '{MessagingConnectionStringName}' connection string was not found. " +
                $"Provide it via the Aspire-provisioned broker resource or a configured " +
                $"ConnectionStrings:{MessagingConnectionStringName} value.");
        }

        return connectionString;
    }

    private static void GuardFormatMatchesBroker(string broker, string connectionString)
    {
        var looksLikeRabbitMq = IsAmqpUri(connectionString);
        var looksLikeAzureServiceBus = IsServiceBusEndpoint(connectionString);

        if (broker == BrokerNames.RabbitMq && !looksLikeRabbitMq)
        {
            throw new InvalidOperationException(
                $"'{MessageBrokerKey}' is '{BrokerNames.RabbitMq}' but the " +
                $"'{MessagingConnectionStringName}' connection string is not an AMQP URI " +
                $"(expected it to start with 'amqp://' or 'amqps://'); it looks like an " +
                $"Azure Service Bus endpoint. Check MessageBroker alignment with the " +
                $"provisioned broker: either set '{MessageBrokerKey}' to " +
                $"'{BrokerNames.AzureServiceBus}' or point '{MessagingConnectionStringName}' " +
                $"at RabbitMQ.");
        }

        if (broker == BrokerNames.AzureServiceBus && !looksLikeAzureServiceBus)
        {
            throw new InvalidOperationException(
                $"'{MessageBrokerKey}' is '{BrokerNames.AzureServiceBus}' but the " +
                $"'{MessagingConnectionStringName}' connection string is not an Azure " +
                $"Service Bus endpoint (expected it to contain 'Endpoint=sb://'); it looks " +
                $"like an AMQP URI. Check MessageBroker alignment with the provisioned " +
                $"broker: either set '{MessageBrokerKey}' to '{BrokerNames.RabbitMq}' or " +
                $"point '{MessagingConnectionStringName}' at Azure Service Bus.");
        }
    }

    private static bool IsAmqpUri(string connectionString) =>
        connectionString.StartsWith("amqp://", StringComparison.OrdinalIgnoreCase) ||
        connectionString.StartsWith("amqps://", StringComparison.OrdinalIgnoreCase);

    private static bool IsServiceBusEndpoint(string connectionString) =>
        connectionString.Contains("Endpoint=sb://", StringComparison.OrdinalIgnoreCase);
}
