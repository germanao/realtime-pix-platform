using Azure.Monitor.OpenTelemetry.AspNetCore;
using IdentityPresence.Api;
using IdentityPresence.Application;
using IdentityPresence.Infrastructure;
using Microsoft.EntityFrameworkCore;
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

builder.Services.AddIdentityPresenceInfrastructure(builder.Configuration);
builder.Services.AddRealtimePixEventBus(builder.Configuration, IdentityPresenceMetadata.ServiceName);
if (!string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("Default")))
{
    builder.Services.AddRealtimePixEfCoreEventing<IdentityPresenceDbContext>();
}

builder.Services.AddSingleton<PresenceBroadcaster>();

var app = builder.Build();
app.UseExceptionHandler();
app.UseCors("browser");

app.MapGet("/health", () => Results.Ok(new { service = IdentityPresenceMetadata.ServiceName, status = "ok" }));
app.MapGet("/health/live", () => Results.Ok(new { service = IdentityPresenceMetadata.ServiceName, status = "live" }));
app.MapGet("/health/ready", async (IIdentityReadinessProbe probe, CancellationToken cancellationToken) =>
{
    var result = await probe.CheckAsync(cancellationToken);
    var body = new
    {
        service = IdentityPresenceMetadata.ServiceName,
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
app.MapHub<PresenceHub>("/presence/hub");

app.MapPost("/sessions/anonymous", async (
    AnonymousSessionRequest request,
    ConnectAnonymousHandler handler,
    PresenceBroadcaster broadcaster,
    CancellationToken cancellationToken) =>
{
    // This endpoint is the browser fallback when a websocket transport is
    // unavailable. Give it a stable synthetic connection so fallback users are
    // represented in the same presence projection as SignalR users.
    var clientId = string.IsNullOrWhiteSpace(request.ClientId)
        ? Guid.NewGuid().ToString("N")
        : request.ClientId.Trim();
    var result = await handler.HandleAsync(clientId, $"http:{clientId}", cancellationToken);
    await broadcaster.BroadcastSnapshotAsync(result.ActiveUsers, cancellationToken);
    return Results.Ok(result.Session);
});

app.MapPost("/presence/heartbeat", async (
    PresenceHeartbeatRequest request,
    HeartbeatPresenceHandler handler,
    CancellationToken cancellationToken) =>
{
    var user = await handler.HandleAsync(request.UserId, cancellationToken);
    return user is null
        ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Unknown user")
        : Results.Ok(user);
});

app.MapPost("/presence/leave", async (
    PresenceLeaveRequest request,
    LeavePresenceHandler handler,
    PresenceBroadcaster broadcaster,
    CancellationToken cancellationToken) =>
{
    var result = await handler.LeaveAsync(request.UserId, request.ConnectionId, cancellationToken);
    if (result is null)
    {
        return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Unknown user");
    }

    await broadcaster.BroadcastSnapshotAsync(result.ActiveUsers, cancellationToken);
    return Results.Ok(result.User);
});

app.MapGet("/presence/users", async (IPresenceStore store, CancellationToken cancellationToken) =>
    Results.Ok(await store.GetActiveUsersAsync(cancellationToken)));

app.Run();

public partial class Program;
