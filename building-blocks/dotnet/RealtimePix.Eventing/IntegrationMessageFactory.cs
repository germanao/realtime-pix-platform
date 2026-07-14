using System.Diagnostics;
using System.Text.Json;

namespace RealtimePix.Eventing;

internal static class IntegrationMessageFactory
{
    public static EventEnvelope Create<TPayload>(
        string messageType,
        int version,
        string producer,
        TPayload payload,
        IntegrationMessageKind messageKind,
        IntegrationMessageDestination destination,
        string? subject,
        string? correlationId,
        string? causationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageType);
        ArgumentException.ThrowIfNullOrWhiteSpace(producer);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination.Name);

        return new EventEnvelope(
            Guid.NewGuid(),
            messageType,
            version,
            DateTimeOffset.UtcNow,
            correlationId ?? Guid.NewGuid().ToString("N"),
            causationId,
            producer,
            JsonSerializer.SerializeToElement(payload, JsonDefaults.Options))
        {
            Subject = subject,
            MessageKind = messageKind.ToString(),
            DestinationKind = destination.Kind.ToString(),
            Destination = destination.Name,
            TraceParent = Activity.Current?.Id
        };
    }
}
