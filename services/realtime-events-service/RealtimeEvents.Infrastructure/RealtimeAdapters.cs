using Microsoft.AspNetCore.SignalR;
using RealtimeEvents.Application;
using RealtimePix.Contracts;
using RealtimePix.Eventing;

namespace RealtimeEvents.Infrastructure;

public sealed class EventsHub(IRealtimeProjectionStore store) : Hub
{
    public override async Task OnConnectedAsync()
    {
        await Clients.Caller.SendAsync(
            "events.timelineSnapshot",
            await store.GetTimelineAsync(Context.ConnectionAborted),
            Context.ConnectionAborted);
        await base.OnConnectedAsync();
    }

    public async Task SubscribeTransfer(string transferId) =>
        await Clients.Caller.SendAsync(
            "events.transferFlowSnapshot",
            await store.GetFlowAsync(transferId, Context.ConnectionAborted),
            Context.ConnectionAborted);
}

public sealed class SignalRProjectionNotifier(IHubContext<EventsHub> hubContext) : IRealtimeProjectionNotifier
{
    public Task TimelineItemAsync(TimelineEventResponse item, CancellationToken cancellationToken) =>
        hubContext.Clients.All.SendAsync("events.timelineItem", item, cancellationToken);

    public Task TransferFlowStepAsync(FlowStepResponse step, CancellationToken cancellationToken) =>
        hubContext.Clients.All.SendAsync("events.transferFlowStep", step, cancellationToken);
}

public sealed class ArchitectureFlowPublisher(IIntegrationEventPublisher publisher) : IArchitectureFlowPublisher
{
    public Task PublishAsync(FlowStepResponse step, PlatformEvent source, CancellationToken cancellationToken) =>
        publisher.PublishAsync(
            EventTypes.ArchitectureFlowStepRecorded,
            1,
            RealtimeEventsMetadata.ServiceName,
            new ArchitectureFlowStepRecordedPayload(
                step.StepId,
                step.TransferId,
                step.EventType,
                step.Stage,
                step.Title,
                step.Detail,
                step.RecordedAt),
            correlationId: source.CorrelationId,
            causationId: source.EventId,
            cancellationToken: cancellationToken);
}

public sealed class PlatformEventProjectionAdapter(ProjectPlatformEventHandler handler) : IIntegrationEventHandler
{
    public IReadOnlyCollection<string> EventTypes { get; } = RealtimePix.Contracts.EventTypes.All;

    public Task HandleAsync(EventEnvelope envelope, CancellationToken cancellationToken) =>
        handler.HandleAsync(
            new PlatformEvent(
                envelope.EventId.ToString("N"),
                envelope.EventType,
                envelope.Producer,
                envelope.CorrelationId,
                envelope.CausationId,
                envelope.OccurredAt,
                envelope.Payload),
            cancellationToken);
}

public static class RealtimeEventsMetadata
{
    public const string ServiceName = "realtime-events-service";
}
