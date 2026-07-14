using System.Text.Json;

namespace RealtimePix.Eventing;

public sealed record EventEnvelope(
    Guid EventId,
    string EventType,
    int Version,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    string? CausationId,
    string Producer,
    JsonElement Payload)
{
    public string SpecVersion { get; init; } = "1.0";

    public string DataContentType { get; init; } = "application/json";

    public string? Subject { get; init; }

    public string MessageKind { get; init; } = IntegrationMessageKind.Event.ToString();

    public string DestinationKind { get; init; } = IntegrationDestinationKind.Topic.ToString();

    public string? Destination { get; init; }

    public string? TraceParent { get; init; }

    public TPayload DeserializePayload<TPayload>()
    {
        return Payload.Deserialize<TPayload>(JsonDefaults.Options)
            ?? throw new InvalidOperationException($"Event payload could not be deserialized as {typeof(TPayload).Name}.");
    }
}
