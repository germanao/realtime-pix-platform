namespace IdentityPresence.Application;

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

public sealed record IdentityReadinessResult(
    bool IsReady,
    bool DatabaseReady,
    bool EventBusReady,
    bool RealtimeReady,
    string? Reason = null);

public sealed record RealtimeTransportReadinessResult(bool IsReady, string Mode, string? Reason = null);
