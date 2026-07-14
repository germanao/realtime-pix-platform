using System.Collections.Concurrent;
using IdentityPresence.Application;
using IdentityPresence.Domain;
using RealtimePix.Contracts;

namespace IdentityPresence.Infrastructure;

public sealed class InMemoryPresenceStore : IPresenceStore
{
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<string, PresenceUser> _users = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _connectionToUser = new(StringComparer.OrdinalIgnoreCase);

    public InMemoryPresenceStore()
    {
        foreach (var bot in KnownBotUsers.All)
        {
            _users[bot.UserId] = new PresenceUser(bot.UserId, bot.DisplayName, true, DateTimeOffset.UtcNow);
        }
    }

    public Task<AnonymousSessionResponse> JoinAnonymousAsync(string? clientId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var identity = CreateIdentity(clientId);
            var user = UpsertUser(identity, DateTimeOffset.UtcNow);
            return Task.FromResult(ToSession(identity, user));
        }
    }

    public Task<PresenceJoinResult> ConnectAnonymousAsync(
        string? clientId,
        string connectionId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        lock (_gate)
        {
            var identity = CreateIdentity(clientId);
            var isNewUser = !_users.ContainsKey(identity.UserId);
            var user = UpsertUser(identity, DateTimeOffset.UtcNow);
            var wasOnline = user.IsOnline;
            user.ConnectionIds.Add(connectionId);
            _connectionToUser[connectionId] = user.UserId;
            var response = ToResponse(user);
            return Task.FromResult(new PresenceJoinResult(
                ToSession(identity, user),
                response,
                isNewUser,
                !wasOnline && response.IsOnline,
                GetActiveUsersCore()));
        }
    }

    public Task<PresenceUserResponse?> HeartbeatAsync(string userId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_users.TryGetValue(userId, out var user))
            {
                return Task.FromResult<PresenceUserResponse?>(null);
            }

            user.LastSeenAt = DateTimeOffset.UtcNow;
            return Task.FromResult<PresenceUserResponse?>(ToResponse(user));
        }
    }

    public Task<PresenceLeaveResult?> LeaveAsync(
        string userId,
        string? connectionId,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_users.TryGetValue(userId, out var user))
            {
                return Task.FromResult<PresenceLeaveResult?>(null);
            }

            var wasOnline = user.IsOnline;
            if (string.IsNullOrWhiteSpace(connectionId))
            {
                foreach (var activeConnection in user.ConnectionIds)
                {
                    _connectionToUser.Remove(activeConnection);
                }

                user.ConnectionIds.Clear();
            }
            else
            {
                user.ConnectionIds.Remove(connectionId);
                _connectionToUser.Remove(connectionId);
            }

            user.LastSeenAt = DateTimeOffset.UtcNow;
            var response = ToResponse(user);
            return Task.FromResult<PresenceLeaveResult?>(new PresenceLeaveResult(
                response,
                wasOnline && !response.IsOnline,
                GetActiveUsersCore()));
        }
    }

    public Task<PresenceLeaveResult?> DisconnectAsync(string connectionId, CancellationToken cancellationToken)
    {
        string? userId;
        lock (_gate)
        {
            _connectionToUser.TryGetValue(connectionId, out userId);
        }

        return userId is null
            ? Task.FromResult<PresenceLeaveResult?>(null)
            : LeaveAsync(userId, connectionId, cancellationToken);
    }

    public Task<IReadOnlyCollection<PresenceUserResponse>> GetActiveUsersAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(GetActiveUsersCore());
        }
    }

    private PresenceUser UpsertUser(AnonymousIdentity identity, DateTimeOffset now)
    {
        if (_users.TryGetValue(identity.UserId, out var existing))
        {
            existing.DisplayName = AnonymousDisplayNames.Create(identity.ClientId);
            existing.LastSeenAt = now;
            return existing;
        }

        var user = new PresenceUser(identity.UserId, AnonymousDisplayNames.Create(identity.ClientId), false, now);
        _users[user.UserId] = user;
        return user;
    }

    private IReadOnlyCollection<PresenceUserResponse> GetActiveUsersCore() =>
        _users.Values
            .Select(ToResponse)
            .Where(user => user.IsOnline)
            .OrderByDescending(user => user.IsBot)
            .ThenBy(user => user.DisplayName)
            .ToArray();

    private static AnonymousIdentity CreateIdentity(string? clientId) =>
        new(string.IsNullOrWhiteSpace(clientId) ? Guid.NewGuid().ToString("N") : clientId);

    private static AnonymousSessionResponse ToSession(AnonymousIdentity identity, PresenceUser user) =>
        new(identity.ClientId, user.UserId, user.DisplayName, Guid.NewGuid().ToString("N"), user.LastSeenAt);

    private static PresenceUserResponse ToResponse(PresenceUser user) =>
        new(user.UserId, user.DisplayName, user.IsBot, user.IsOnline, user.LastSeenAt);

    private sealed class PresenceUser(string userId, string displayName, bool isBot, DateTimeOffset lastSeenAt)
    {
        public string UserId { get; } = userId;

        public string DisplayName { get; set; } = displayName;

        public bool IsBot { get; } = isBot;

        public DateTimeOffset LastSeenAt { get; set; } = lastSeenAt;

        public HashSet<string> ConnectionIds { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool IsOnline => IsBot || ConnectionIds.Count > 0;
    }
}
