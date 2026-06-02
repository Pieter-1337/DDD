namespace BuildingBlocks.Application.Messaging;

/// <summary>
/// The validated outcome of broker selection: which broker a service runs on
/// and the <c>messaging</c> connection string that targets it.
/// </summary>
/// <param name="Broker">
/// The resolved broker name, guaranteed to be one of <see cref="BrokerNames"/>.
/// </param>
/// <param name="ConnectionString">
/// The resolved <c>messaging</c> connection string, guaranteed non-empty and
/// validated to match the format expected by <paramref name="Broker"/>.
/// </param>
public sealed record BrokerSelection(string Broker, string ConnectionString);
