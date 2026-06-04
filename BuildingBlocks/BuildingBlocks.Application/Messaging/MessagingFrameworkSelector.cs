using Microsoft.Extensions.Configuration;

namespace BuildingBlocks.Application.Messaging;

/// <summary>
/// The single deep module that owns the messaging-framework decision for the
/// whole system. Given a service's configuration it produces a validated
/// framework name (MassTransit or Wolverine) or throws a descriptive startup
/// exception that names the unrecognized value and the valid options.
///
/// This module owns:
/// <list type="bullet">
///   <item>reading the per-service <c>MessagingFramework</c> value, defaulting
///   to <see cref="MessagingFrameworkNames.MassTransit"/> when unset (per
///   PRD #24);</item>
///   <item>validating that the value is one of the recognized frameworks;</item>
///   <item>the fail-fast guard that an unrecognized value throws at startup.</item>
/// </list>
///
/// It has no dependency on any messaging framework; the framework extensions
/// consume it.
/// </summary>
public static class MessagingFrameworkSelector
{
    /// <summary>The configuration key holding the per-service messaging-framework choice.</summary>
    public const string MessagingFrameworkKey = "MessagingFramework";

    /// <summary>
    /// Resolves and validates the messaging-framework selection from configuration.
    /// </summary>
    /// <param name="configuration">The service's configuration.</param>
    /// <returns>The validated framework name (MassTransit or Wolverine).</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="configuration"/> is null.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The <c>MessagingFramework</c> value is unrecognized.
    /// </exception>
    public static string Resolve(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return ResolveFrameworkName(configuration);
    }

    private static string ResolveFrameworkName(IConfiguration configuration)
    {
        // Indexer (not GetValue<T>) so this module needs only
        // Microsoft.Extensions.Configuration.Abstractions.
        var configured = configuration[MessagingFrameworkKey];

        // Unset / blank => default to MassTransit (per PRD #24).
        if (string.IsNullOrWhiteSpace(configured))
        {
            return MessagingFrameworkNames.MassTransit;
        }

        var trimmed = configured.Trim();

        if (string.Equals(trimmed, MessagingFrameworkNames.MassTransit, StringComparison.OrdinalIgnoreCase))
        {
            return MessagingFrameworkNames.MassTransit;
        }

        if (string.Equals(trimmed, MessagingFrameworkNames.Wolverine, StringComparison.OrdinalIgnoreCase))
        {
            return MessagingFrameworkNames.Wolverine;
        }

        throw new InvalidOperationException(
            $"Unrecognized '{MessagingFrameworkKey}' value '{configured}'. " +
            $"Valid values are '{MessagingFrameworkNames.MassTransit}' and '{MessagingFrameworkNames.Wolverine}'.");
    }
}
