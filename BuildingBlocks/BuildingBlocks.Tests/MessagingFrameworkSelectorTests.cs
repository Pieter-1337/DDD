using BuildingBlocks.Application.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace BuildingBlocks.Tests;

/// <summary>
/// Tests for <see cref="MessagingFrameworkSelector"/>: the deep module that
/// owns the messaging-framework decision. These exercise the module's public
/// contract only (configuration in -> validated framework name or descriptive
/// exception out) and run with no messaging framework dependency.
/// </summary>
[TestClass]
public class MessagingFrameworkSelectorTests
{
    private static IConfiguration BuildConfiguration(string? framework)
    {
        var values = new Dictionary<string, string?>();

        if (framework is not null)
        {
            values["MessagingFramework"] = framework;
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    [TestMethod]
    public void Resolve_Should_DefaultToMassTransit_WhenMessagingFrameworkUnset()
    {
        // Arrange
        var configuration = BuildConfiguration(framework: null);

        // Act
        var framework = MessagingFrameworkSelector.Resolve(configuration);

        // Assert
        framework.ShouldBe(MessagingFrameworkNames.MassTransit);
    }

    [TestMethod]
    public void Resolve_Should_DefaultToMassTransit_WhenMessagingFrameworkBlank()
    {
        // Arrange
        var configuration = BuildConfiguration(framework: "   ");

        // Act
        var framework = MessagingFrameworkSelector.Resolve(configuration);

        // Assert
        framework.ShouldBe(MessagingFrameworkNames.MassTransit);
    }

    [TestMethod]
    public void Resolve_Should_SelectMassTransit_WhenExplicitlyConfigured()
    {
        // Arrange
        var configuration = BuildConfiguration(MessagingFrameworkNames.MassTransit);

        // Act
        var framework = MessagingFrameworkSelector.Resolve(configuration);

        // Assert
        framework.ShouldBe(MessagingFrameworkNames.MassTransit);
    }

    [TestMethod]
    public void Resolve_Should_SelectWolverine_WhenExplicitlyConfigured()
    {
        // Arrange
        var configuration = BuildConfiguration(MessagingFrameworkNames.Wolverine);

        // Act
        var framework = MessagingFrameworkSelector.Resolve(configuration);

        // Assert
        framework.ShouldBe(MessagingFrameworkNames.Wolverine);
    }

    [TestMethod]
    public void Resolve_Should_BeCaseInsensitive_ForFrameworkName()
    {
        // Arrange
        var configuration = BuildConfiguration("wolverine");

        // Act
        var framework = MessagingFrameworkSelector.Resolve(configuration);

        // Assert
        framework.ShouldBe(MessagingFrameworkNames.Wolverine);
    }

    [TestMethod]
    public void Resolve_Should_Throw_ListingValidValues_WhenFrameworkUnrecognized()
    {
        // Arrange
        var configuration = BuildConfiguration("Kafka");

        // Act
        var ex = Should.Throw<InvalidOperationException>(() => MessagingFrameworkSelector.Resolve(configuration));

        // Assert - message names the bad value and lists the valid ones
        ex.Message.ShouldContain("Kafka");
        ex.Message.ShouldContain(MessagingFrameworkNames.MassTransit);
        ex.Message.ShouldContain(MessagingFrameworkNames.Wolverine);
    }

    [TestMethod]
    public void Resolve_Should_Throw_WhenConfigurationIsNull()
    {
        // Act / Assert
        Should.Throw<ArgumentNullException>(() => MessagingFrameworkSelector.Resolve(null!));
    }
}
