using System.Text.Json;
using RealtimePix.Contracts;
using RealtimePix.Eventing;
using Xunit;

public sealed class RealtimeProjectionStoreTests
{
    [Fact]
    public void Duplicate_timeline_event_id_is_ignored()
    {
        var store = new global::RealtimeProjectionStore();
        var item = CreateTimelineItem("event-1", EventTypes.PixTransferRequested, "transfer-1");

        Assert.True(store.TryAddTimeline(item));
        Assert.False(store.TryAddTimeline(item with { Producer = "duplicate" }));

        var timelineItem = Assert.Single(store.GetTimeline());
        Assert.Equal("event-1", timelineItem.EventId);
        Assert.Equal("test", timelineItem.Producer);
    }

    [Fact]
    public void Transfer_flow_contains_one_step_per_source_event()
    {
        var store = new global::RealtimeProjectionStore();
        var first = new global::FlowStepResponse(
            "step-1",
            "transfer-1",
            EventTypes.PixDebitSucceeded,
            "wallet-ledger-service",
            "PixDebitSucceeded",
            "Debit persisted.",
            DateTimeOffset.UtcNow,
            "event-1",
            "wallet-ledger-service",
            "correlation-1",
            "cause-1",
            "success");
        var duplicate = first with { StepId = "step-2" };

        Assert.True(store.TryAddFlowStep("event-1", first));
        Assert.False(store.TryAddFlowStep("event-1", duplicate));

        var step = Assert.Single(store.GetFlow("transfer-1"));
        Assert.Equal("step-1", step.StepId);
        Assert.Equal("event-1", step.SourceEventId);
        Assert.Equal("wallet-ledger-service", step.Producer);
        Assert.Equal("correlation-1", step.CorrelationId);
        Assert.Equal("cause-1", step.CausationId);
        Assert.Equal("success", step.Outcome);
    }

    [Fact]
    public void Presence_events_are_added_to_public_timeline()
    {
        var store = new global::RealtimeProjectionStore();
        var item = CreateTimelineItem("presence-event-1", EventTypes.UserPresenceChanged, null);

        Assert.True(store.TryAddTimeline(item));

        var projected = Assert.Single(store.GetTimeline());
        Assert.Equal(EventTypes.UserPresenceChanged, projected.EventType);
        Assert.Null(projected.TransferId);
    }

    private static global::TimelineEventResponse CreateTimelineItem(string eventId, string eventType, string? transferId)
    {
        var payload = transferId is null
            ? JsonSerializer.SerializeToElement(new UserPresenceChangedPayload("user-a", "Azure Ledger", true, false, DateTimeOffset.UtcNow), JsonDefaults.Options)
            : JsonSerializer.SerializeToElement(new PixTransferRequestedPayload(transferId, "key", "sender", "sender_bank-a", "recipient", "recipient_bank-a", 10m), JsonDefaults.Options);

        return new global::TimelineEventResponse(
            eventId,
            eventType,
            "test",
            transferId,
            "correlation-1",
            DateTimeOffset.UtcNow,
            payload);
    }
}
