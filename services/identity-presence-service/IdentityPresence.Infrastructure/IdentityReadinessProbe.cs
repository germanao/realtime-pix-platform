using System.Data.Common;
using IdentityPresence.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RealtimePix.Eventing;

namespace IdentityPresence.Infrastructure;

internal interface IIdentityDatabaseReadinessProbe
{
    Task<bool> CheckAsync(CancellationToken cancellationToken);
}

internal sealed class LocalIdentityDatabaseReadinessProbe : IIdentityDatabaseReadinessProbe
{
    public Task<bool> CheckAsync(CancellationToken cancellationToken) => Task.FromResult(true);
}

internal sealed class EfIdentityDatabaseReadinessProbe(
    IdentityPresenceDbContext dbContext,
    ILogger<EfIdentityDatabaseReadinessProbe> logger) : IIdentityDatabaseReadinessProbe
{
    public async Task<bool> CheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await dbContext.Database.CanConnectAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "The identity PostgreSQL readiness probe failed.");
            return false;
        }
    }
}

internal sealed class IdentityReadinessProbe(
    IIdentityDatabaseReadinessProbe database,
    IEventBusReadinessProbe eventBus,
    IRealtimeTransportReadinessProbe realtime) : IIdentityReadinessProbe
{
    public async Task<IdentityReadinessResult> CheckAsync(CancellationToken cancellationToken)
    {
        var databaseTask = database.CheckAsync(cancellationToken);
        var eventBusTask = eventBus.CheckAsync(cancellationToken);
        var realtimeTask = realtime.CheckAsync(cancellationToken);
        await Task.WhenAll(databaseTask, eventBusTask, realtimeTask);

        var databaseReady = await databaseTask;
        var eventBusReady = (await eventBusTask).IsReady;
        var realtimeReady = (await realtimeTask).IsReady;
        var reason = !databaseReady
            ? "database-unavailable"
            : !eventBusReady
                ? "event-bus-unavailable"
                : !realtimeReady
                    ? "signalr-unavailable"
                    : null;
        return new IdentityReadinessResult(
            databaseReady && eventBusReady,
            databaseReady,
            eventBusReady,
            realtimeReady,
            reason);
    }
}
