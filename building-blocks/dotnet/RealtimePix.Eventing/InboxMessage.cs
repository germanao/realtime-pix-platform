namespace RealtimePix.Eventing;

public sealed record InboxMessage(
    Guid EventId,
    string EventType,
    string ConsumerName,
    DateTimeOffset ReceivedAt,
    DateTimeOffset? ProcessedAt,
    string? Error);
