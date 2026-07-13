using System.Collections.Concurrent;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using RealtimePix.Contracts;
using RealtimePix.Eventing;

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
    builder.Services.AddSingleton<PresenceStore>();
    builder.Services.AddSingleton<IPresenceStore>(serviceProvider =>
        new InMemoryPresenceStoreAdapter(serviceProvider.GetRequiredService<PresenceStore>()));
}
else
{
    builder.Services.AddDbContext<IdentityPresenceDbContext>(options => options.UseNpgsql(defaultConnectionString));
    builder.Services.AddScoped<IPresenceStore, EfPresenceStore>();
}

builder.Services.AddSingleton<PresenceBroadcaster>();
builder.Services.AddRealtimePixEventBus(builder.Configuration, IdentityPresenceServiceMetadata.Name);
if (!string.IsNullOrWhiteSpace(defaultConnectionString))
{
    builder.Services.AddRealtimePixEfCoreEventing<IdentityPresenceDbContext>();
}

var app = builder.Build();
app.UseCors("browser");

app.MapGet("/health", () => Results.Ok(new { service = IdentityPresenceServiceMetadata.Name, status = "ok" }));
app.MapGet("/health/live", () => Results.Ok(new { service = IdentityPresenceServiceMetadata.Name, status = "live" }));
app.MapGet("/health/ready", () => Results.Ok(new { service = IdentityPresenceServiceMetadata.Name, status = "ready" }));
app.MapHub<PresenceHub>("/presence/hub");

app.MapPost("/sessions/anonymous", async (
    AnonymousSessionRequest request,
    IPresenceStore store,
    IIntegrationEventPublisher publisher,
    CancellationToken cancellationToken) =>
{
    var session = await store.JoinAnonymousAsync(request.ClientId, cancellationToken);

    await publisher.PublishAsync(
        EventTypes.AnonymousUserJoined,
        1,
        IdentityPresenceServiceMetadata.Name,
        new AnonymousUserJoinedPayload(session.UserId, session.DisplayName, session.ClientId, false),
        correlationId: session.UserId,
        cancellationToken: cancellationToken);

    await publisher.PublishAsync(
        EventTypes.UserPresenceChanged,
        1,
        IdentityPresenceServiceMetadata.Name,
        new UserPresenceChangedPayload(session.UserId, session.DisplayName, true, false, session.LastSeenAt),
        correlationId: session.UserId,
        cancellationToken: cancellationToken);

    return Results.Ok(session);
});

app.MapPost("/presence/heartbeat", async (
    PresenceHeartbeatRequest request,
    IPresenceStore store,
    IIntegrationEventPublisher publisher,
    CancellationToken cancellationToken) =>
{
    var user = await store.HeartbeatAsync(request.UserId, cancellationToken);
    if (user is null)
    {
        return Results.NotFound(new { message = "Unknown user." });
    }

    await publisher.PublishAsync(
        EventTypes.UserPresenceChanged,
        1,
        IdentityPresenceServiceMetadata.Name,
        new UserPresenceChangedPayload(user.UserId, user.DisplayName, true, user.IsBot, user.LastSeenAt),
        correlationId: user.UserId,
        cancellationToken: cancellationToken);

    return Results.Ok(user);
});

app.MapPost("/presence/leave", async (
    PresenceLeaveRequest request,
    IPresenceStore store,
    PresenceBroadcaster broadcaster,
    IIntegrationEventPublisher publisher,
    CancellationToken cancellationToken) =>
{
    var result = await store.LeaveAsync(request.UserId, request.ConnectionId, cancellationToken);
    if (result is null)
    {
        return Results.NotFound(new { message = "Unknown user." });
    }

    if (result.BecameOffline && result.User is not null)
    {
        await publisher.PublishAsync(
            EventTypes.UserPresenceChanged,
            1,
            IdentityPresenceServiceMetadata.Name,
            new UserPresenceChangedPayload(result.User.UserId, result.User.DisplayName, false, result.User.IsBot, result.User.LastSeenAt),
            correlationId: result.User.UserId,
            cancellationToken: cancellationToken);
    }

    await broadcaster.BroadcastSnapshotAsync(result.ActiveUsers, cancellationToken);
    return Results.Ok(result.User);
});

app.MapGet("/presence/users", async (IPresenceStore store, CancellationToken cancellationToken) =>
    Results.Ok(await store.GetActiveUsersAsync(cancellationToken)));

app.Run();

public sealed record AnonymousSessionRequest(string? ClientId);

public sealed record PresenceHeartbeatRequest(string UserId);

public sealed record PresenceLeaveRequest(string UserId, string? ConnectionId);

public sealed record AnonymousSessionResponse(
    string ClientId,
    string UserId,
    string DisplayName,
    string SessionToken,
    DateTimeOffset LastSeenAt);

public sealed record PresenceUserResponse(
    string UserId,
    string DisplayName,
    bool IsBot,
    bool IsOnline,
    DateTimeOffset LastSeenAt);

public sealed record PresenceJoinResult(
    AnonymousSessionResponse Session,
    PresenceUserResponse User,
    bool IsNewUser,
    bool BecameOnline,
    IReadOnlyCollection<PresenceUserResponse> ActiveUsers);

public sealed record PresenceLeaveResult(
    PresenceUserResponse? User,
    bool BecameOffline,
    IReadOnlyCollection<PresenceUserResponse> ActiveUsers);

public interface IPresenceStore
{
    Task<AnonymousSessionResponse> JoinAnonymousAsync(string? clientId, CancellationToken cancellationToken);

    Task<PresenceJoinResult> ConnectAnonymousAsync(string? clientId, string connectionId, CancellationToken cancellationToken);

    Task<PresenceUserResponse?> HeartbeatAsync(string userId, CancellationToken cancellationToken);

    Task<PresenceLeaveResult?> LeaveAsync(string userId, string? connectionId, CancellationToken cancellationToken);

    Task<PresenceLeaveResult?> DisconnectAsync(string connectionId, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<PresenceUserResponse>> GetActiveUsersAsync(CancellationToken cancellationToken);
}

public sealed class InMemoryPresenceStoreAdapter(PresenceStore inner) : IPresenceStore
{
    public Task<AnonymousSessionResponse> JoinAnonymousAsync(string? clientId, CancellationToken cancellationToken)
    {
        return Task.FromResult(inner.JoinAnonymous(clientId));
    }

    public Task<PresenceJoinResult> ConnectAnonymousAsync(string? clientId, string connectionId, CancellationToken cancellationToken)
    {
        return Task.FromResult(inner.ConnectAnonymous(clientId, connectionId));
    }

    public Task<PresenceUserResponse?> HeartbeatAsync(string userId, CancellationToken cancellationToken)
    {
        return Task.FromResult(inner.Heartbeat(userId));
    }

    public Task<PresenceLeaveResult?> LeaveAsync(string userId, string? connectionId, CancellationToken cancellationToken)
    {
        return Task.FromResult(inner.Leave(userId, connectionId));
    }

    public Task<PresenceLeaveResult?> DisconnectAsync(string connectionId, CancellationToken cancellationToken)
    {
        return Task.FromResult(inner.Disconnect(connectionId));
    }

    public Task<IReadOnlyCollection<PresenceUserResponse>> GetActiveUsersAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(inner.GetActiveUsers());
    }
}

public sealed class PresenceStore
{
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<string, PresenceUser> _users = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _connectionToUser = new(StringComparer.OrdinalIgnoreCase);

    public PresenceStore()
    {
        foreach (var bot in KnownBotUsers.All)
        {
            _users[bot.UserId] = new PresenceUser(bot.UserId, bot.DisplayName, null, true, DateTimeOffset.UtcNow);
        }
    }

    public AnonymousSessionResponse JoinAnonymous(string? clientId)
    {
        var normalizedClientId = string.IsNullOrWhiteSpace(clientId) ? Guid.NewGuid().ToString("N") : clientId.Trim();
        var userId = $"user-{normalizedClientId}";
        var now = DateTimeOffset.UtcNow;
        var displayName = CreateDisplayName(normalizedClientId);

        lock (_gate)
        {
            var user = UpsertUser(userId, displayName, normalizedClientId, false, now);

            return new AnonymousSessionResponse(
                normalizedClientId,
                user.UserId,
                user.DisplayName,
                Guid.NewGuid().ToString("N"),
                user.LastSeenAt);
        }
    }

    public PresenceJoinResult ConnectAnonymous(string? clientId, string connectionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);

        var normalizedClientId = string.IsNullOrWhiteSpace(clientId) ? Guid.NewGuid().ToString("N") : clientId.Trim();
        var userId = $"user-{normalizedClientId}";
        var now = DateTimeOffset.UtcNow;
        var displayName = CreateDisplayName(normalizedClientId);

        lock (_gate)
        {
            var isNewUser = !_users.ContainsKey(userId);
            var user = UpsertUser(userId, displayName, normalizedClientId, false, now);
            var wasOnline = IsOnline(user);

            user.ConnectionIds.Add(connectionId);
            user.LastSeenAt = now;
            _connectionToUser[connectionId] = user.UserId;

            var response = ToResponse(user);
            var session = new AnonymousSessionResponse(
                normalizedClientId,
                user.UserId,
                user.DisplayName,
                Guid.NewGuid().ToString("N"),
                user.LastSeenAt);

            return new PresenceJoinResult(session, response, isNewUser, !wasOnline && response.IsOnline, GetActiveUsersCore());
        }
    }

    public PresenceUserResponse? Heartbeat(string userId)
    {
        lock (_gate)
        {
            if (!_users.TryGetValue(userId, out var user))
            {
                return null;
            }

            user.LastSeenAt = DateTimeOffset.UtcNow;
            return ToResponse(user);
        }
    }

    public PresenceLeaveResult? Leave(string userId, string? connectionId)
    {
        lock (_gate)
        {
            if (!_users.TryGetValue(userId, out var user))
            {
                return null;
            }

            var wasOnline = IsOnline(user);
            if (string.IsNullOrWhiteSpace(connectionId))
            {
                user.ConnectionIds.Clear();
                foreach (var item in _connectionToUser.Where(pair => pair.Value == userId).Select(pair => pair.Key).ToArray())
                {
                    _connectionToUser.Remove(item);
                }
            }
            else
            {
                user.ConnectionIds.Remove(connectionId);
                _connectionToUser.Remove(connectionId);
            }

            user.LastSeenAt = DateTimeOffset.UtcNow;
            var response = ToResponse(user);
            return new PresenceLeaveResult(response, wasOnline && !response.IsOnline, GetActiveUsersCore());
        }
    }

    public PresenceLeaveResult? Disconnect(string connectionId)
    {
        lock (_gate)
        {
            if (!_connectionToUser.TryGetValue(connectionId, out var userId))
            {
                return null;
            }

            return Leave(userId, connectionId);
        }
    }

    public IReadOnlyCollection<PresenceUserResponse> GetActiveUsers()
    {
        lock (_gate)
        {
            return GetActiveUsersCore();
        }
    }

    private PresenceUser UpsertUser(string userId, string displayName, string? clientId, bool isBot, DateTimeOffset now)
    {
        if (_users.TryGetValue(userId, out var existing))
        {
            existing.DisplayName = displayName;
            existing.LastSeenAt = now;
            return existing;
        }

        var user = new PresenceUser(userId, displayName, clientId, isBot, now);
        _users[user.UserId] = user;
        return user;
    }

    private IReadOnlyCollection<PresenceUserResponse> GetActiveUsersCore()
    {
        return _users.Values
            .Select(ToResponse)
            .Where(user => user.IsOnline)
            .OrderByDescending(user => user.IsBot)
            .ThenBy(user => user.DisplayName)
            .ToArray();
    }

    private static bool IsOnline(PresenceUser user)
    {
        return user.IsBot || user.ConnectionIds.Count > 0;
    }

    private static PresenceUserResponse ToResponse(PresenceUser user)
    {
        return new PresenceUserResponse(user.UserId, user.DisplayName, user.IsBot, IsOnline(user), user.LastSeenAt);
    }

    public static string CreateDisplayName(string clientId)
    {
        return AnonymousDisplayNames.Create(clientId);
    }

    private sealed class PresenceUser(
        string UserId,
        string DisplayName,
        string? ClientId,
        bool IsBot,
        DateTimeOffset lastSeenAt)
    {
        public string UserId { get; } = UserId;
        public string DisplayName { get; set; } = DisplayName;
        public string? ClientId { get; } = ClientId;
        public bool IsBot { get; } = IsBot;
        public DateTimeOffset LastSeenAt { get; set; } = lastSeenAt;
        public HashSet<string> ConnectionIds { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}

public sealed class PresenceBroadcaster(IHubContext<PresenceHub> hubContext)
{
    public Task BroadcastSnapshotAsync(IReadOnlyCollection<PresenceUserResponse> users, CancellationToken cancellationToken)
    {
        return hubContext.Clients.All.SendAsync("presence.snapshot", users, cancellationToken);
    }
}

public sealed class PresenceHub(
    IPresenceStore store,
    PresenceBroadcaster broadcaster,
    IIntegrationEventPublisher publisher) : Hub
{
    public async Task<AnonymousSessionResponse> Join(AnonymousSessionRequest request)
    {
        var result = await store.ConnectAnonymousAsync(request.ClientId, Context.ConnectionId, Context.ConnectionAborted);

        if (result.IsNewUser)
        {
            await publisher.PublishAsync(
                EventTypes.AnonymousUserJoined,
                1,
                IdentityPresenceServiceMetadata.Name,
                new AnonymousUserJoinedPayload(result.Session.UserId, result.Session.DisplayName, result.Session.ClientId, false),
                correlationId: result.Session.UserId,
                cancellationToken: Context.ConnectionAborted);
        }

        if (result.BecameOnline)
        {
            await publisher.PublishAsync(
                EventTypes.UserPresenceChanged,
                1,
                IdentityPresenceServiceMetadata.Name,
                new UserPresenceChangedPayload(result.User.UserId, result.User.DisplayName, true, result.User.IsBot, result.User.LastSeenAt),
                correlationId: result.User.UserId,
                cancellationToken: Context.ConnectionAborted);
        }

        await broadcaster.BroadcastSnapshotAsync(result.ActiveUsers, Context.ConnectionAborted);
        return result.Session;
    }

    public async Task Leave(PresenceLeaveRequest request)
    {
        var result = await store.LeaveAsync(request.UserId, request.ConnectionId ?? Context.ConnectionId, Context.ConnectionAborted);
        if (result is null)
        {
            return;
        }

        if (result.BecameOffline && result.User is not null)
        {
            await publisher.PublishAsync(
                EventTypes.UserPresenceChanged,
                1,
                IdentityPresenceServiceMetadata.Name,
                new UserPresenceChangedPayload(result.User.UserId, result.User.DisplayName, false, result.User.IsBot, result.User.LastSeenAt),
                correlationId: result.User.UserId,
                cancellationToken: Context.ConnectionAborted);
        }

        await broadcaster.BroadcastSnapshotAsync(result.ActiveUsers, Context.ConnectionAborted);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var result = await store.DisconnectAsync(Context.ConnectionId, CancellationToken.None);
        if (result?.BecameOffline == true && result.User is not null)
        {
            await publisher.PublishAsync(
                EventTypes.UserPresenceChanged,
                1,
                IdentityPresenceServiceMetadata.Name,
                new UserPresenceChangedPayload(result.User.UserId, result.User.DisplayName, false, result.User.IsBot, result.User.LastSeenAt),
                correlationId: result.User.UserId,
                cancellationToken: CancellationToken.None);
        }

        if (result is not null)
        {
            await broadcaster.BroadcastSnapshotAsync(result.ActiveUsers, CancellationToken.None);
        }

        await base.OnDisconnectedAsync(exception);
    }
}

public static class IdentityPresenceServiceMetadata
{
    public const string Name = "identity-presence-service";
}

