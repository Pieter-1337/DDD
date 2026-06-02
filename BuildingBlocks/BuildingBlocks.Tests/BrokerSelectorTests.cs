using BuildingBlocks.Application.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace BuildingBlocks.Tests;

/// <summary>
/// Tests for <see cref="BrokerSelector"/>: the deep module that owns the
/// message-broker decision. These exercise the module's public contract only
/// (configuration in -> validated selection or descriptive exception out) and
/// run with no broker, no container, and no messaging-framework dependency.
/// </summary>
[TestClass]
public class BrokerSelectorTests
{
    private const string RabbitConnectionString = "amqp://guest:guest@localhost:5672";

    private const string AzureServiceBusConnectionString =
        "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;" +
        "SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";

    private static IConfiguration BuildConfiguration(string? broker, string? messagingConnectionString)
    {
        var values = new Dictionary<string, string?>();

        if (broker is not null)
        {
            values["MessageBroker"] = broker;
        }

        if (messagingConnectionString is not null)
        {
            values["ConnectionStrings:messaging"] = messagingConnectionString;
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    [TestMethod]
    public void Resolve_Should_DefaultToRabbitMq_WhenMessageBrokerUnset()
    {
        // Arrange
        var configuration = BuildConfiguration(broker: null, RabbitConnectionString);

        // Act
        var selection = BrokerSelector.Resolve(configuration);

        // Assert
        selection.Broker.ShouldBe(BrokerNames.RabbitMq);
        selection.ConnectionString.ShouldBe(RabbitConnectionString);
    }

    [TestMethod]
    public void Resolve_Should_DefaultToRabbitMq_WhenMessageBrokerBlank()
    {
        // Arrange
        var configuration = BuildConfiguration(broker: "   ", RabbitConnectionString);

        // Act
        var selection = BrokerSelector.Resolve(configuration);

        // Assert
        selection.Broker.ShouldBe(BrokerNames.RabbitMq);
    }

    [TestMethod]
    public void Resolve_Should_SelectRabbitMq_WhenExplicitlyConfigured()
    {
        // Arrange
        var configuration = BuildConfiguration(BrokerNames.RabbitMq, RabbitConnectionString);

        // Act
        var selection = BrokerSelector.Resolve(configuration);

        // Assert
        selection.Broker.ShouldBe(BrokerNames.RabbitMq);
        selection.ConnectionString.ShouldBe(RabbitConnectionString);
    }

    [TestMethod]
    public void Resolve_Should_SelectAzureServiceBus_WhenExplicitlyConfigured()
    {
        // Arrange
        var configuration = BuildConfiguration(BrokerNames.AzureServiceBus, AzureServiceBusConnectionString);

        // Act
        var selection = BrokerSelector.Resolve(configuration);

        // Assert
        selection.Broker.ShouldBe(BrokerNames.AzureServiceBus);
        selection.ConnectionString.ShouldBe(AzureServiceBusConnectionString);
    }

    [TestMethod]
    public void Resolve_Should_BeCaseInsensitive_ForBrokerName()
    {
        // Arrange
        var configuration = BuildConfiguration("azureservicebus", AzureServiceBusConnectionString);

        // Act
        var selection = BrokerSelector.Resolve(configuration);

        // Assert
        selection.Broker.ShouldBe(BrokerNames.AzureServiceBus);
    }

    [TestMethod]
    public void Resolve_Should_Throw_ListingValidValues_WhenBrokerUnrecognized()
    {
        // Arrange - no connection string needed; broker validation happens first
        var configuration = BuildConfiguration("Kafka", messagingConnectionString: null);

        // Act
        var ex = Should.Throw<InvalidOperationException>(() => BrokerSelector.Resolve(configuration));

        // Assert - message names the bad value and lists the valid ones
        ex.Message.ShouldContain("Kafka");
        ex.Message.ShouldContain(BrokerNames.RabbitMq);
        ex.Message.ShouldContain(BrokerNames.AzureServiceBus);
    }

    [TestMethod]
    public void Resolve_Should_Throw_WhenMessagingConnectionStringMissing()
    {
        // Arrange
        var configuration = BuildConfiguration(BrokerNames.RabbitMq, messagingConnectionString: null);

        // Act
        var ex = Should.Throw<InvalidOperationException>(() => BrokerSelector.Resolve(configuration));

        // Assert
        ex.Message.ShouldContain("messaging");
    }

    [TestMethod]
    public void Resolve_Should_Throw_WhenRabbitMqGivenServiceBusConnectionString()
    {
        // Arrange - broker says RabbitMq but the connection string is ASB-format
        var configuration = BuildConfiguration(BrokerNames.RabbitMq, AzureServiceBusConnectionString);

        // Act
        var ex = Should.Throw<InvalidOperationException>(() => BrokerSelector.Resolve(configuration));

        // Assert - names the misalignment and the canonical phrase from the spec
        ex.Message.ShouldContain(BrokerNames.RabbitMq);
        ex.Message.ShouldContain("Check MessageBroker alignment with the provisioned broker");
    }

    [TestMethod]
    public void Resolve_Should_Throw_WhenAzureServiceBusGivenAmqpConnectionString()
    {
        // Arrange - broker says AzureServiceBus but the connection string is AMQP
        var configuration = BuildConfiguration(BrokerNames.AzureServiceBus, RabbitConnectionString);

        // Act
        var ex = Should.Throw<InvalidOperationException>(() => BrokerSelector.Resolve(configuration));

        // Assert - names the misalignment and the canonical phrase from the spec
        ex.Message.ShouldContain(BrokerNames.AzureServiceBus);
        ex.Message.ShouldContain("Check MessageBroker alignment with the provisioned broker");
    }

    [TestMethod]
    public void Resolve_Should_AcceptAmqpsUri_ForRabbitMq()
    {
        // Arrange - TLS AMQP URIs are valid RabbitMQ connection strings
        var configuration = BuildConfiguration(BrokerNames.RabbitMq, "amqps://user:pass@host:5671");

        // Act
        var selection = BrokerSelector.Resolve(configuration);

        // Assert
        selection.Broker.ShouldBe(BrokerNames.RabbitMq);
    }

    [TestMethod]
    public void Resolve_Should_Throw_WhenConfigurationIsNull()
    {
        // Act / Assert
        Should.Throw<ArgumentNullException>(() => BrokerSelector.Resolve(null!));
    }
}
