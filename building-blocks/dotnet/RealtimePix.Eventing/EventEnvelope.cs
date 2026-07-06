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
    public TPayload DeserializePayload<TPayload>()
    {
        return Payload.Deserialize<TPayload>(JsonDefaults.Options)
            ?? throw new InvalidOperationException($"Event payload could not be deserialized as {typeof(TPayload).Name}.");
    }
}

