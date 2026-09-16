using IdentityPresence.Application;
using Microsoft.AspNetCore.SignalR;

namespace IdentityPresence.Api;

/// <summary>Exercise the SDK's authenticated server connection, never an unauthenticated root URL.</summary>
internal sealed class HubTransportReadinessProbe(IHubContext<PresenceHub> hub) : IRealtimeTransportReadinessProbe
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private RealtimeTransportReadinessResult? _cached;
    private DateTimeOffset _expiresAt;

    public async Task<RealtimeTransportReadinessResult> CheckAsync(CancellationToken cancellationToken)
    {
        if (_cached is not null && DateTimeOffset.UtcNow < _expiresAt) return _cached;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_cached is not null && DateTimeOffset.UtcNow < _expiresAt) return _cached;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            try
            {
                // An empty group has no recipients and creates no browser events.
                await hub.Clients.Group("__readiness_no_members__")
                    .SendAsync("__readiness", cancellationToken: timeout.Token);
                _cached = new(true, "sdk");
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                _cached = new(false, "sdk", "live-updates-unavailable");
            }
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(_cached.IsReady ? 15 : 3);
            return _cached;
        }
        finally { _gate.Release(); }
    }
}
