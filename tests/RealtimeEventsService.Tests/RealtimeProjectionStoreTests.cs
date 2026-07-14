using System.Text.Json;
using RealtimeEvents.Application;
using RealtimeEvents.Infrastructure;
using RealtimePix.Contracts;
using RealtimePix.Eventing;
using Xunit;

public sealed class RealtimeProjectionStoreTests
{
    [Fact]
    public async Task Duplicate_timeline_event_id_is_ignored()
    {
        var store = new InMemoryRealtimeProjectionStore();
        var item = CreateTimelineItem("event-1", EventTypes.PixTransferRequested, "transfer-1");

        Assert.True(await store.TryAddTimelineAsync(item, CancellationToken.None));
        Assert.False(await store.TryAddTimelineAsync(item with { Producer = "duplicate" }, CancellationToken.None));

        var timelineItem = Assert.Single(await store.GetTimelineAsync(CancellationToken.None));
        Assert.Equal("event-1", timelineItem.EventId);
        Assert.Equal("test", timelineItem.Producer);
    }

    [Fact]
    public async Task Transfer_flow_contains_one_step_per_source_event()
    {
        var store = new InMemoryRealtimeProjectionStore();
        var first = new FlowStepResponse(
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

        Assert.True(await store.TryAddFlowStepAsync("event-1", first, CancellationToken.None));
        Assert.False(await store.TryAddFlowStepAsync("event-1", duplicate, CancellationToken.None));

        var step = Assert.Single(await store.GetFlowAsync("transfer-1", CancellationToken.None));
        Assert.Equal("step-1", step.StepId);
        Assert.Equal("event-1", step.SourceEventId);
        Assert.Equal("wallet-ledger-service", step.Producer);
        Assert.Equal("correlation-1", step.CorrelationId);
        Assert.Equal("cause-1", step.CausationId);
        Assert.Equal("success", step.Outcome);
    }

    [Fact]
    public async Task Presence_events_are_added_to_public_timeline()
    {
        var store = new InMemoryRealtimeProjectionStore();
        var item = CreateTimelineItem("presence-event-1", EventTypes.UserPresenceChanged, null);

        Assert.True(await store.TryAddTimelineAsync(item, CancellationToken.None));

        var projected = Assert.Single(await store.GetTimelineAsync(CancellationToken.None));
        Assert.Equal(EventTypes.UserPresenceChanged, projected.EventType);
        Assert.Null(projected.TransferId);
    }

    [Fact]
    public async Task Replay_repairs_a_missing_flow_step_without_reannouncing_the_timeline_item()
    {
        var store = new InMemoryRealtimeProjectionStore();
        var occurredAt = DateTimeOffset.UtcNow;
        var payload = JsonSerializer.SerializeToElement(
            new FundsDebitedPayload("transfer-1", "bank-a", "account-a", "sender", 10m, 90m),
            JsonDefaults.Options);
        var platformEvent = new PlatformEvent(
            "event-1",
            EventTypes.FundsDebited,
            "bank-a-ledger-service",
            "transfer-1",
            "command-1",
            occurredAt,
            payload);
        await store.TryAddTimelineAsync(
            new TimelineEventResponse(
                platformEvent.EventId,
                platformEvent.EventType,
                platformEvent.Producer,
                "transfer-1",
                platformEvent.CorrelationId,
                platformEvent.OccurredAt,
                platformEvent.Payload),
            CancellationToken.None);
        var notifier = new RecordingNotifier();
        var publisher = new RecordingPublisher();
        var handler = new ProjectPlatformEventHandler(
            store,
            notifier,
            publisher,
            new NoopRealtimeProjectionTransaction());

        await handler.HandleAsync(platformEvent, CancellationToken.None);

        Assert.Empty(notifier.TimelineItems);
        Assert.Single(notifier.FlowSteps);
        Assert.Single(publisher.FlowSteps);
        Assert.Single(await store.GetFlowAsync("transfer-1", CancellationToken.None));
    }

    private static TimelineEventResponse CreateTimelineItem(string eventId, string eventType, string? transferId)
    {
        var payload = transferId is null
            ? JsonSerializer.SerializeToElement(new UserPresenceChangedPayload("user-a", "Azure Ledger", true, false, DateTimeOffset.UtcNow), JsonDefaults.Options)
            : JsonSerializer.SerializeToElement(new PixTransferRequestedPayload(transferId, "key", "sender", "sender_bank-a", "recipient", "recipient_bank-a", 10m), JsonDefaults.Options);

        return new TimelineEventResponse(
            eventId,
            eventType,
            "test",
            transferId,
            "correlation-1",
            DateTimeOffset.UtcNow,
            payload);
    }

    private sealed class RecordingNotifier : IRealtimeProjectionNotifier
    {
        public List<TimelineEventResponse> TimelineItems { get; } = [];

        public List<FlowStepResponse> FlowSteps { get; } = [];

        public Task TimelineItemAsync(TimelineEventResponse item, CancellationToken cancellationToken)
        {
            TimelineItems.Add(item);
            return Task.CompletedTask;
        }

        public Task TransferFlowStepAsync(FlowStepResponse step, CancellationToken cancellationToken)
        {
            FlowSteps.Add(step);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingPublisher : IArchitectureFlowPublisher
    {
        public List<FlowStepResponse> FlowSteps { get; } = [];

        public Task PublishAsync(FlowStepResponse step, PlatformEvent source, CancellationToken cancellationToken)
        {
            FlowSteps.Add(step);
            return Task.CompletedTask;
        }
    }
}
