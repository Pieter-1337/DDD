using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Infrastructure.Wolverine;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace BuildingBlocks.Tests;

/// <summary>
/// Tests for the Wolverine MassTransit-interop fail-fast guard (RabbitMQ-only by design,
/// ADR-0001). Exercise the pure guard method directly — no broker, container, or host.
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
