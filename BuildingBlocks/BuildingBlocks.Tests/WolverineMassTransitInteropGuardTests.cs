using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Infrastructure.Wolverine;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace BuildingBlocks.Tests;

/// <summary>
/// Tests for the Wolverine MassTransit-interop fail-fast guard. The interop bridge
/// (<c>ListenToMassTransitQueue</c>) is RabbitMQ-only by design (ADR-0001); configuring it
/// while the broker is Azure Service Bus must throw a descriptive startup exception naming
/// the supported alternative. These exercise the pure guard method directly, so they need no
/// running broker, no container, and no Wolverine host.
/// </summary>
[TestClass]
public class WolverineMassTransitInteropGuardTests
{
    [TestMethod]
    public void Guard_Should_Throw_WhenBrokerIsAzureServiceBus()
    {
        // Act
        var ex = Should.Throw<InvalidOperationException>(() =>
            WolverineExtensions.GuardMassTransitInteropSupported(BrokerNames.AzureServiceBus));

        // Assert — the message names the broker, the RabbitMQ-only constraint, and the fix
        ex.Message.ShouldContain(BrokerNames.AzureServiceBus);
        ex.Message.ShouldContain("RabbitMQ-only");
        ex.Message.ShouldContain("MessagingFramework=MassTransit");
    }

    [TestMethod]
    public void Guard_Should_NotThrow_WhenBrokerIsRabbitMq()
    {
        // Act / Assert — RabbitMQ is the supported interop broker
        Should.NotThrow(() =>
            WolverineExtensions.GuardMassTransitInteropSupported(BrokerNames.RabbitMq));
    }

    [TestMethod]
    public void Guard_Should_NotThrow_WhenBrokerIsUnknown()
    {
        // Arrange / Act / Assert — a null ambient broker means the helper was called
        // outside the AddWolverineEventBus flow; the guard is a no-op since the broker
        // is unknown (RabbitMQ wiring surfaces any real misconfiguration on its own).
        Should.NotThrow(() =>
            WolverineExtensions.GuardMassTransitInteropSupported(null));
    }
}
