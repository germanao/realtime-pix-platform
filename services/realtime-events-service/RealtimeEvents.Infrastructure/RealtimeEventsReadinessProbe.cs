using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RealtimeEvents.Application;
using RealtimePix.Eventing;

namespace RealtimeEvents.Infrastructure;

internal interface IRealtimeDatabaseReadinessProbe
{
    Task<bool> CheckAsync(CancellationToken cancellationToken);
}

internal sealed class LocalRealtimeDatabaseReadinessProbe : IRealtimeDatabaseReadinessProbe
{
    public Task<bool> CheckAsync(CancellationToken cancellationToken) => Task.FromResult(true);
}

internal sealed class EfRealtimeDatabaseReadinessProbe(
    RealtimeProjectionDbContext dbContext,
    ILogger<EfRealtimeDatabaseReadinessProbe> logger) : IRealtimeDatabaseReadinessProbe
{
    public async Task<bool> CheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await dbContext.Database.CanConnectAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "The realtime projection PostgreSQL readiness probe failed.");
            return false;
        }
    }
}

internal sealed class RealtimeEventsReadinessProbe(
    IRealtimeDatabaseReadinessProbe database,
    IEventBusReadinessProbe eventBus,
    IRealtimeTransportReadinessProbe realtime) : IRealtimeEventsReadinessProbe
{
    public async Task<RealtimeEventsReadinessResult> CheckAsync(CancellationToken cancellationToken)
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
        return new RealtimeEventsReadinessResult(
            databaseReady && eventBusReady,
            databaseReady,
            eventBusReady,
            realtimeReady,
            reason);
    }
}
