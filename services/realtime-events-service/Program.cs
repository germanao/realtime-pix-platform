using System.Text.Json;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using RealtimePix.Contracts;
using RealtimePix.Eventing;

const string ServiceName = "realtime-events-service";

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddRealtimePixAzureAppConfiguration();
builder.Services.AddCors(options =>
{
    options.AddPolicy("browser", policy =>
        policy.SetIsOriginAllowed(origin => CorsOrigins.IsAllowed(origin, builder.Configuration))
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials());
});
builder.Services.AddOpenTelemetry().UseAzureMonitor();
var signalRBuilder = builder.Services.AddSignalR();
var azureSignalRConnectionString = builder.Configuration["AzureSignalR:ConnectionString"];
if (!string.IsNullOrWhiteSpace(azureSignalRConnectionString))
{
    signalRBuilder.AddAzureSignalR(azureSignalRConnectionString);
}

var defaultConnectionString = builder.Configuration.GetConnectionString("Default");
if (string.IsNullOrWhiteSpace(defaultConnectionString))
{
    builder.Services.AddSingleton<RealtimeProjectionStore>();
    builder.Services.AddSingleton<IRealtimeProjectionStore>(serviceProvider =>
        new InMemoryRealtimeProjectionStoreAdapter(serviceProvider.GetRequiredService<RealtimeProjectionStore>()));
    builder.Services.AddSingleton<IIntegrationEventHandler, PlatformEventProjectionHandler>();
}
else
{
    builder.Services.AddDbContext<RealtimeProjectionDbContext>(options => options.UseNpgsql(defaultConnectionString));
    builder.Services.AddScoped<IRealtimeProjectionStore, EfRealtimeProjectionStore>();
    builder.Services.AddScoped<IIntegrationEventHandler, PlatformEventProjectionHandler>();
}

builder.Services.AddRealtimePixEventBus(builder.Configuration, ServiceName);
if (!string.IsNullOrWhiteSpace(defaultConnectionString))
{
    builder.Services.AddRealtimePixEfCoreEventing<RealtimeProjectionDbContext>();
}

var app = builder.Build();
app.UseCors("browser");

app.MapGet("/health", () => Results.Ok(new { service = ServiceName, status = "ok" }));
app.MapGet("/health/live", () => Results.Ok(new { service = ServiceName, status = "live" }));
app.MapGet("/health/ready", () => Results.Ok(new { service = ServiceName, status = "ready" }));
app.MapHub<EventsHub>("/events/hub");
app.MapGet("/events/timeline", async (IRealtimeProjectionStore store, CancellationToken cancellationToken) =>
    Results.Ok(await store.GetTimelineAsync(cancellationToken)));
app.MapGet("/events/transfers/{transferId}/flow", async (string transferId, IRealtimeProjectionStore store, CancellationToken cancellationToken) =>
    Results.Ok(await store.GetFlowAsync(transferId, cancellationToken)));
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

public interface IRealtimeProjectionStore
{
    Task<bool> TryAddTimelineAsync(TimelineEventResponse item, CancellationToken cancellationToken);

    Task<bool> TryAddFlowStepAsync(string sourceEventId, FlowStepResponse step, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<TimelineEventResponse>> GetTimelineAsync(CancellationToken cancellationToken);

    Task<IReadOnlyCollection<FlowStepResponse>> GetFlowAsync(string transferId, CancellationToken cancellationToken);
}

public sealed class InMemoryRealtimeProjectionStoreAdapter(RealtimeProjectionStore inner) : IRealtimeProjectionStore
{
    public Task<bool> TryAddTimelineAsync(TimelineEventResponse item, CancellationToken cancellationToken)
    {
        return Task.FromResult(inner.TryAddTimeline(item));
    }

    public Task<bool> TryAddFlowStepAsync(string sourceEventId, FlowStepResponse step, CancellationToken cancellationToken)
    {
        return Task.FromResult(inner.TryAddFlowStep(sourceEventId, step));
    }

    public Task<IReadOnlyCollection<TimelineEventResponse>> GetTimelineAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(inner.GetTimeline());
    }

    public Task<IReadOnlyCollection<FlowStepResponse>> GetFlowAsync(string transferId, CancellationToken cancellationToken)
    {
        return Task.FromResult(inner.GetFlow(transferId));
    }
}

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

public sealed class EventsHub(IRealtimeProjectionStore store) : Hub
{
    public override async Task OnConnectedAsync()
    {
        await Clients.Caller.SendAsync("events.timelineSnapshot", await store.GetTimelineAsync(Context.ConnectionAborted), Context.ConnectionAborted);
        await base.OnConnectedAsync();
    }

    public async Task SubscribeTransfer(string transferId)
    {
        await Clients.Caller.SendAsync("events.transferFlowSnapshot", await store.GetFlowAsync(transferId, Context.ConnectionAborted), Context.ConnectionAborted);
    }
}

public sealed class PlatformEventProjectionHandler(
    IRealtimeProjectionStore store,
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

        if (!await store.TryAddTimelineAsync(timelineItem, cancellationToken))
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
        var addedFlowStep = await store.TryAddFlowStepAsync(sourceEventId, step, cancellationToken);
        if (addedFlowStep)
        {
            await hubContext.Clients.All.SendAsync("events.transferFlowStep", step, cancellationToken);

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
