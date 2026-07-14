using System.Text.Json;

namespace RealtimeEvents.Application;

public sealed record TimelineEventResponse(
    string EventId,
    string EventType,
    string Producer,
    string? TransferId,
    string CorrelationId,
    DateTimeOffset OccurredAt,
    JsonElement Payload);

public sealed record FlowStepResponse(
    string StepId,
    string? TransferId,
    string EventType,
    string Stage,
    string Title,
    string Detail,
    DateTimeOffset RecordedAt,
    string SourceEventId = "",
    string Producer = "",
    string CorrelationId = "",
    string? CausationId = null,
    string Outcome = "info");

public sealed record PlatformEvent(
    string EventId,
    string EventType,
    string Producer,
    string CorrelationId,
    string? CausationId,
    DateTimeOffset OccurredAt,
    JsonElement Payload);

public sealed record RealtimeEventsReadinessResult(
    bool IsReady,
    bool DatabaseReady,
    bool EventBusReady,
    bool RealtimeReady,
    string? Reason = null);

public sealed record RealtimeTransportReadinessResult(bool IsReady, string Mode, string? Reason = null);
