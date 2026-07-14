using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.EntityFrameworkCore;
using RealtimeEvents.Api;
using RealtimeEvents.Application;
using RealtimeEvents.Infrastructure;
using RealtimePix.Eventing;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddRealtimePixAzureAppConfiguration();
builder.Services.AddProblemDetails();
builder.Services.AddCors(options =>
{
    options.AddPolicy("browser", policy =>
        policy.SetIsOriginAllowed(origin => CorsOrigins.IsAllowed(origin, builder.Configuration))
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials());
});
if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    builder.Services.AddOpenTelemetry().UseAzureMonitor();
}

var signalRBuilder = builder.Services.AddSignalR();
var azureSignalRConnectionString = builder.Configuration["AzureSignalR:ConnectionString"];
if (!string.IsNullOrWhiteSpace(azureSignalRConnectionString))
{
    signalRBuilder.AddAzureSignalR(azureSignalRConnectionString);
}
else if (builder.Configuration["AzureSignalR:Endpoint"] is { Length: > 0 } signalREndpoint)
{
    var clientId = builder.Configuration["AZURE_CLIENT_ID"]
        ?? throw new InvalidOperationException("AZURE_CLIENT_ID is required for Azure SignalR managed identity authentication.");
    signalRBuilder.AddAzureSignalR(
        $"Endpoint={signalREndpoint};AuthType=azure.msi;ClientId={clientId};Version=1.0;");
}

builder.Services.AddRealtimeEventsInfrastructure(builder.Configuration);
builder.Services.AddRealtimePixEventBus(builder.Configuration, RealtimeEventsMetadata.ServiceName);
if (!string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("Default")))
{
    builder.Services.AddRealtimePixEfCoreEventing<RealtimeProjectionDbContext>();
}

var app = builder.Build();
app.UseExceptionHandler();
app.UseCors("browser");

app.MapGet("/health", () => Results.Ok(new { service = RealtimeEventsMetadata.ServiceName, status = "ok" }));
app.MapGet("/health/live", () => Results.Ok(new { service = RealtimeEventsMetadata.ServiceName, status = "live" }));
app.MapGet("/health/ready", async (IRealtimeEventsReadinessProbe probe, CancellationToken cancellationToken) =>
{
    var result = await probe.CheckAsync(cancellationToken);
    var body = new
    {
        service = RealtimeEventsMetadata.ServiceName,
        status = result.IsReady ? "ready" : "not-ready",
        dependencies = new
        {
            database = result.DatabaseReady,
            eventBus = result.EventBusReady,
            realtime = result.RealtimeReady
        },
        reason = result.Reason
    };
    return result.IsReady
        ? Results.Ok(body)
        : Results.Json(body, statusCode: StatusCodes.Status503ServiceUnavailable);
});
app.MapHub<EventsHub>("/events/hub");
app.MapGet("/events/timeline", async (IRealtimeProjectionStore store, CancellationToken cancellationToken) =>
    Results.Ok(await store.GetTimelineAsync(cancellationToken)));
app.MapGet("/events/transfers/{transferId}/flow", async (
    string transferId,
    IRealtimeProjectionStore store,
    CancellationToken cancellationToken) =>
    Results.Ok(await store.GetFlowAsync(transferId, cancellationToken)));
app.MapGet("/realtime/token", () => Results.Ok(new
{
    mode = string.IsNullOrWhiteSpace(azureSignalRConnectionString) ? "direct-signalr" : "azure-signalr",
    channel = "public-event-timeline",
    expiresAt = DateTimeOffset.UtcNow.AddMinutes(30)
}));

app.Run();

public partial class Program;
