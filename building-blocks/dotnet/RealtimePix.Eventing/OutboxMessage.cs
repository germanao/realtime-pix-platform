namespace RealtimePix.Eventing;

public sealed record OutboxMessage(
    Guid Id,
    string EventType,
    string PayloadJson,
    DateTimeOffset OccurredAt,
    DateTimeOffset? PublishedAt,
    int PublishAttempts);

