using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using RealtimePix.Contracts;
using RealtimePix.Eventing;

const string ServiceName = "realtime-events-service";

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCors(options =>
{
    options.AddPolicy("browser", policy =>
        policy.SetIsOriginAllowed(_ => true)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials());
});
var signalRBuilder = builder.Services.AddSignalR();
var azureSignalRConnectionString = builder.Configuration["AzureSignalR:ConnectionString"];
if (!string.IsNullOrWhiteSpace(azureSignalRConnectionString))
{
    signalRBuilder.AddAzureSignalR(azureSignalRConnectionString);
}

builder.Services.AddSingleton<RealtimeProjectionStore>();
builder.Services.AddSingleton<IIntegrationEventHandler, PlatformEventProjectionHandler>();
builder.Services.AddRealtimePixEventBus(builder.Configuration, ServiceName);

var app = builder.Build();
app.UseCors("browser");

app.MapGet("/health", () => Results.Ok(new { service = ServiceName, status = "ok" }));
app.MapGet("/health/live", () => Results.Ok(new { service = ServiceName, status = "live" }));
app.MapGet("/health/ready", () => Results.Ok(new { service = ServiceName, status = "ready" }));
app.MapHub<EventsHub>("/events/hub");
app.MapGet("/events/timeline", (RealtimeProjectionStore store) => Results.Ok(store.GetTimeline()));
app.MapGet("/events/transfers/{transferId}/flow", (string transferId, RealtimeProjectionStore store) => Results.Ok(store.GetFlow(transferId)));
app.MapGet("/realtime/token", () => Results.Ok(new
{
    mode = "local-jsonl",
    channel = "public-event-timeline",
    expiresAt = DateTimeOffset.UtcNow.AddMinutes(30)
}));

app.Run();

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

public sealed class RealtimeProjectionStore
{
    private readonly object _gate = new();
    private readonly List<TimelineEventResponse> _timeline = [];
    private readonly Dictionary<string, List<FlowStepResponse>> _flowByTransfer = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _seenTimelineEventIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _seenFlowSourceEventIds = new(StringComparer.OrdinalIgnoreCase);

    public bool TryAddTimeline(TimelineEventResponse item)
    {
        lock (_gate)
        {
            if (!_seenTimelineEventIds.Add(item.EventId))
            {
                return false;
            }

            _timeline.Add(item);
            if (_timeline.Count > 250)
            {
                _seenTimelineEventIds.Remove(_timeline[0].EventId);
                _timeline.RemoveAt(0);
            }

            return true;
        }
    }

    public bool TryAddFlowStep(string sourceEventId, FlowStepResponse step)
    {
        if (string.IsNullOrWhiteSpace(step.TransferId))
        {
            return false;
        }

        lock (_gate)
        {
            if (!_seenFlowSourceEventIds.Add(sourceEventId))
            {
                return false;
            }

            if (!_flowByTransfer.TryGetValue(step.TransferId, out var steps))
            {
                steps = [];
                _flowByTransfer[step.TransferId] = steps;
            }

            steps.Add(step);
            return true;
        }
    }

    public IReadOnlyCollection<TimelineEventResponse> GetTimeline()
    {
        lock (_gate)
        {
            return _timeline.OrderByDescending(item => item.OccurredAt).ToArray();
        }
    }

    public IReadOnlyCollection<FlowStepResponse> GetFlow(string transferId)
    {
        lock (_gate)
        {
            return _flowByTransfer.TryGetValue(transferId, out var steps)
                ? steps.OrderBy(step => step.RecordedAt).ToArray()
                : [];
        }
    }
}

public static class RealtimeEventsServiceMetadata
{
    public const string Name = "realtime-events-service";
}

public sealed class EventsHub(RealtimeProjectionStore store) : Hub
{
    public override async Task OnConnectedAsync()
    {
        await Clients.Caller.SendAsync("events.timelineSnapshot", store.GetTimeline(), Context.ConnectionAborted);
        await base.OnConnectedAsync();
    }

    public async Task SubscribeTransfer(string transferId)
    {
        await Clients.Caller.SendAsync("events.transferFlowSnapshot", store.GetFlow(transferId), Context.ConnectionAborted);
    }
}

public sealed class PlatformEventProjectionHandler(
    RealtimeProjectionStore store,
    IIntegrationEventPublisher publisher,
    IHubContext<EventsHub> hubContext) : IIntegrationEventHandler
{
    public IReadOnlyCollection<string> EventTypes { get; } = RealtimePix.Contracts.EventTypes.All;

    public async Task HandleAsync(EventEnvelope envelope, CancellationToken cancellationToken)
    {
        var transferId = TryGetString(envelope.Payload, "transferId");
        var timelineItem = new TimelineEventResponse(
            envelope.EventId.ToString("N"),
            envelope.EventType,
            envelope.Producer,
            transferId,
            envelope.CorrelationId,
            envelope.OccurredAt,
            envelope.Payload);

        if (!store.TryAddTimeline(timelineItem))
        {
            return;
        }

        await hubContext.Clients.All.SendAsync("events.timelineItem", timelineItem, cancellationToken);

        if (envelope.EventType == RealtimePix.Contracts.EventTypes.ArchitectureFlowStepRecorded)
        {
            return;
        }

        var step = CreateStep(envelope, transferId);
        var sourceEventId = envelope.EventId.ToString("N");
        var addedFlowStep = store.TryAddFlowStep(sourceEventId, step);
        if (addedFlowStep)
        {
            await hubContext.Clients.All.SendAsync("events.transferFlowStep", step, cancellationToken);
        }

        await publisher.PublishAsync(
            RealtimePix.Contracts.EventTypes.ArchitectureFlowStepRecorded,
            1,
            RealtimeEventsServiceMetadata.Name,
            new ArchitectureFlowStepRecordedPayload(
                step.StepId,
                step.TransferId,
                step.EventType,
                step.Stage,
                step.Title,
                step.Detail,
                step.RecordedAt),
            correlationId: envelope.CorrelationId,
            causationId: envelope.EventId.ToString("N"),
            cancellationToken: cancellationToken);
    }

    private static FlowStepResponse CreateStep(EventEnvelope envelope, string? transferId)
    {
        var stage = envelope.EventType switch
        {
            RealtimePix.Contracts.EventTypes.PixTransferRequested => "transaction-service",
            RealtimePix.Contracts.EventTypes.PixDebitSucceeded => "wallet-ledger-service",
            RealtimePix.Contracts.EventTypes.PixDebitFailed => "wallet-ledger-service",
            RealtimePix.Contracts.EventTypes.PixCreditSucceeded => "wallet-ledger-service",
            RealtimePix.Contracts.EventTypes.PixTransferCompleted => "transaction-service",
            RealtimePix.Contracts.EventTypes.PixTransferFailed => "transaction-service",
            RealtimePix.Contracts.EventTypes.FundsDeposited => "wallet-ledger-service",
            RealtimePix.Contracts.EventTypes.UserPresenceChanged => "identity-presence-service",
            _ => envelope.Producer
        };

        var title = envelope.EventType.Replace(".v1", string.Empty, StringComparison.OrdinalIgnoreCase);
        var detail = envelope.EventType switch
        {
            RealtimePix.Contracts.EventTypes.PixTransferRequested => "The transfer command was accepted and published as an integration event.",
            RealtimePix.Contracts.EventTypes.PixDebitSucceeded => "The wallet service consumed the transfer event and persisted a debit ledger entry.",
            RealtimePix.Contracts.EventTypes.PixDebitFailed => "The wallet service rejected the debit and emitted a failure event.",
            RealtimePix.Contracts.EventTypes.PixCreditSucceeded => "The recipient account was credited from the consumed event.",
            RealtimePix.Contracts.EventTypes.PixTransferCompleted => "The transaction saga observed credit success and marked the transfer completed.",
            RealtimePix.Contracts.EventTypes.PixTransferFailed => "The transaction saga observed a failure event and closed the transfer as failed.",
            _ => "The platform projected this event to the public real-time timeline."
        };

        var outcome = envelope.EventType switch
        {
            RealtimePix.Contracts.EventTypes.PixDebitFailed => "failure",
            RealtimePix.Contracts.EventTypes.PixTransferFailed => "failure",
            RealtimePix.Contracts.EventTypes.PixTransferRequested => "pending",
            RealtimePix.Contracts.EventTypes.PixDebitSucceeded => "success",
            RealtimePix.Contracts.EventTypes.PixCreditSucceeded => "success",
            RealtimePix.Contracts.EventTypes.PixTransferCompleted => "success",
            _ => "info"
        };

        return new FlowStepResponse(
            Guid.NewGuid().ToString("N"),
            transferId,
            envelope.EventType,
            stage,
            title,
            detail,
            DateTimeOffset.UtcNow,
            envelope.EventId.ToString("N"),
            envelope.Producer,
            envelope.CorrelationId,
            envelope.CausationId,
            outcome);
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var property)
            ? property.GetString()
            : null;
    }
}
