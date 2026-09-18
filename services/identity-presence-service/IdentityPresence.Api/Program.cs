using IdentityPresence.Api;
using IdentityPresence.Application;
using IdentityPresence.Infrastructure;
using Microsoft.EntityFrameworkCore;
using RealtimePix.Eventing;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddProblemDetails();
builder.Services.AddCors(options =>
{
    options.AddPolicy("browser", policy =>
        policy.SetIsOriginAllowed(origin => CorsOrigins.IsAllowed(origin, builder.Configuration))
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials());
});
builder.Services.AddSignalR();

builder.Services.AddIdentityPresenceInfrastructure(builder.Configuration);
builder.Services.AddSingleton<IRealtimeTransportReadinessProbe, HubTransportReadinessProbe>();
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
    // Idempotent browser identity with a separate, renewable lease for each tab.
    var clientId = string.IsNullOrWhiteSpace(request.ClientId)
        ? Guid.NewGuid().ToString("N")
        : request.ClientId.Trim();
    if (clientId.Length > 100 || request.TabId?.Length > 36)
    {
        return Results.BadRequest(new { message = "Invalid client or tab identifier." });
    }
    var connectionId = string.IsNullOrWhiteSpace(request.TabId)
        ? $"http:{clientId}" : $"http:{clientId}:{request.TabId}";
    var result = await handler.HandleAsync(clientId, connectionId, cancellationToken);
    var broadcast = broadcaster.BroadcastSnapshotAsync(result.ActiveUsers, CancellationToken.None);
    _ = broadcast.ContinueWith(static _ => { }, TaskContinuationOptions.OnlyOnFaulted);
    return Results.Ok(result.Session);
});

app.MapPost("/presence/heartbeat", async (
    PresenceHeartbeatRequest request,
    HeartbeatPresenceHandler handler,
    CancellationToken cancellationToken) =>
{
    var user = await handler.HandleAsync(request.UserId, cancellationToken, request.ConnectionId);
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
