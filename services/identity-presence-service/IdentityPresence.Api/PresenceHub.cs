using IdentityPresence.Application;
using Microsoft.AspNetCore.SignalR;

namespace IdentityPresence.Api;

public sealed class PresenceBroadcaster(IHubContext<PresenceHub> hubContext)
{
    public Task BroadcastSnapshotAsync(
        IReadOnlyCollection<PresenceUserResponse> users,
        CancellationToken cancellationToken) =>
        hubContext.Clients.All.SendAsync("presence.snapshot", users, cancellationToken);
}

public sealed class PresenceHub(
    ConnectAnonymousHandler connectHandler,
    LeavePresenceHandler leaveHandler,
    PresenceBroadcaster broadcaster) : Hub
{
    public async Task<AnonymousSessionResponse> Join(AnonymousSessionRequest request)
    {
        var result = await connectHandler.HandleAsync(request.ClientId, Context.ConnectionId, Context.ConnectionAborted);
        await broadcaster.BroadcastSnapshotAsync(result.ActiveUsers, Context.ConnectionAborted);
        return result.Session;
    }

    public async Task Leave(PresenceLeaveRequest request)
    {
        var result = await leaveHandler.LeaveAsync(
            request.UserId,
            request.ConnectionId ?? Context.ConnectionId,
            Context.ConnectionAborted);
        if (result is not null)
        {
            await broadcaster.BroadcastSnapshotAsync(result.ActiveUsers, Context.ConnectionAborted);
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var result = await leaveHandler.DisconnectAsync(Context.ConnectionId, CancellationToken.None);
        if (result is not null)
        {
            await broadcaster.BroadcastSnapshotAsync(result.ActiveUsers, CancellationToken.None);
        }

        await base.OnDisconnectedAsync(exception);
    }
}
