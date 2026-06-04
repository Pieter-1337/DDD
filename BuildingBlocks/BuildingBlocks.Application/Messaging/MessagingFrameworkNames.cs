namespace BuildingBlocks.Application.Messaging;

/// <summary>
/// Canonical messaging-framework names. Defined once here so the literal strings
/// cannot drift between the orchestrator, the services, and the framework
/// extensions. These are the only accepted values for the per-service
/// <c>MessagingFramework</c> configuration setting.
/// </summary>
public static class MessagingFrameworkNames
{
    /// <summary>MassTransit (the system default).</summary>
    public const string MassTransit = "MassTransit";

    /// <summary>Wolverine.</summary>
    public const string Wolverine = "Wolverine";
}
