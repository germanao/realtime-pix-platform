namespace IdentityPresence.Application;

public sealed class JoinAnonymousHandler(
    IPresenceStore store,
    IPresenceEventPublisher events,
    IPresenceTransaction transaction)
{
    public async Task<AnonymousSessionResponse> HandleAsync(string? clientId, CancellationToken cancellationToken)
    {
        AnonymousSessionResponse? session = null;
        await transaction.ExecuteAsync(async innerCancellationToken =>
        {
            session = await store.JoinAnonymousAsync(clientId, innerCancellationToken);
            await events.UserJoinedAsync(session, innerCancellationToken);
            await events.PresenceChangedAsync(
                new PresenceUserResponse(session.UserId, session.DisplayName, false, true, session.LastSeenAt),
                true,
                innerCancellationToken);
        }, cancellationToken);
        return session ?? throw new InvalidOperationException("Anonymous session creation produced no result.");
    }
}

public sealed class ConnectAnonymousHandler(
    IPresenceStore store,
    IPresenceEventPublisher events,
    IPresenceTransaction transaction)
{
    public async Task<PresenceJoinResult> HandleAsync(string? clientId, string connectionId, CancellationToken cancellationToken)
    {
        PresenceJoinResult? result = null;
        await transaction.ExecuteAsync(async innerCancellationToken =>
        {
            result = await store.ConnectAnonymousAsync(clientId, connectionId, innerCancellationToken);
            if (result.IsNewUser)
            {
                await events.UserJoinedAsync(result.Session, innerCancellationToken);
            }

            if (result.BecameOnline)
            {
                await events.PresenceChangedAsync(result.User, true, innerCancellationToken);
            }
        }, cancellationToken);

        return result ?? throw new InvalidOperationException("Presence connection produced no result.");
    }
}

public sealed class HeartbeatPresenceHandler(
    IPresenceStore store,
    IPresenceEventPublisher events,
    IPresenceTransaction transaction)
{
    public async Task<PresenceUserResponse?> HandleAsync(string userId, CancellationToken cancellationToken)
    {
        PresenceUserResponse? user = null;
        await transaction.ExecuteAsync(async innerCancellationToken =>
        {
            user = await store.HeartbeatAsync(userId, innerCancellationToken);
            if (user is not null)
            {
                await events.PresenceChangedAsync(user, true, innerCancellationToken);
            }
        }, cancellationToken);

        return user;
    }
}

public sealed class LeavePresenceHandler(
    IPresenceStore store,
    IPresenceEventPublisher events,
    IPresenceTransaction transaction)
{
    public Task<PresenceLeaveResult?> LeaveAsync(string userId, string? connectionId, CancellationToken cancellationToken) =>
        CompleteAsync(token => store.LeaveAsync(userId, connectionId, token), cancellationToken);

    public Task<PresenceLeaveResult?> DisconnectAsync(string connectionId, CancellationToken cancellationToken) =>
        CompleteAsync(token => store.DisconnectAsync(connectionId, token), cancellationToken);

    private async Task<PresenceLeaveResult?> CompleteAsync(
        Func<CancellationToken, Task<PresenceLeaveResult?>> operation,
        CancellationToken cancellationToken)
    {
        PresenceLeaveResult? result = null;
        await transaction.ExecuteAsync(async innerCancellationToken =>
        {
            result = await operation(innerCancellationToken);
            if (result?.BecameOffline == true && result.User is not null)
            {
                await events.PresenceChangedAsync(result.User, false, innerCancellationToken);
            }
        }, cancellationToken);

        return result;
    }
}
