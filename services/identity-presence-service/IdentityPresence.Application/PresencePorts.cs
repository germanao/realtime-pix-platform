namespace IdentityPresence.Application;

public interface IPresenceStore
{
    Task<AnonymousSessionResponse> JoinAnonymousAsync(string? clientId, CancellationToken cancellationToken);

    Task<PresenceJoinResult> ConnectAnonymousAsync(string? clientId, string connectionId, CancellationToken cancellationToken);

    Task<PresenceUserResponse?> HeartbeatAsync(string userId, CancellationToken cancellationToken);

    Task<PresenceLeaveResult?> LeaveAsync(string userId, string? connectionId, CancellationToken cancellationToken);

    Task<PresenceLeaveResult?> DisconnectAsync(string connectionId, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<PresenceUserResponse>> GetActiveUsersAsync(CancellationToken cancellationToken);
}

public interface IPresenceEventPublisher
{
    Task UserJoinedAsync(AnonymousSessionResponse session, CancellationToken cancellationToken);

    Task PresenceChangedAsync(PresenceUserResponse user, bool isOnline, CancellationToken cancellationToken);
}

public interface IPresenceTransaction
{
    Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken);
}

public interface IIdentityReadinessProbe
{
    Task<IdentityReadinessResult> CheckAsync(CancellationToken cancellationToken);
}

public interface IRealtimeTransportReadinessProbe
{
    Task<RealtimeTransportReadinessResult> CheckAsync(CancellationToken cancellationToken);
}
