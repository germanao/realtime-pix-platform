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
            databaseReady && eventBusReady && realtimeReady,
            databaseReady,
            eventBusReady,
            realtimeReady,
            reason);
    }
}

internal sealed class AzureSignalRReadinessProbe(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<AzureSignalRReadinessProbe> logger) : IRealtimeTransportReadinessProbe
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private RealtimeTransportReadinessResult? _cached;
    private DateTimeOffset _cachedAt;

    public async Task<RealtimeTransportReadinessResult> CheckAsync(CancellationToken cancellationToken)
    {
        if (_cached is not null && DateTimeOffset.UtcNow - _cachedAt < CacheDuration)
        {
            return _cached;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_cached is not null && DateTimeOffset.UtcNow - _cachedAt < CacheDuration)
            {
                return _cached;
            }

            _cached = await CheckCoreAsync(cancellationToken);
            _cachedAt = DateTimeOffset.UtcNow;
            return _cached;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<RealtimeTransportReadinessResult> CheckCoreAsync(CancellationToken cancellationToken)
    {
        Uri? endpoint;
        try
        {
            endpoint = ResolveEndpoint(configuration);
        }
        catch (ArgumentException exception)
        {
            logger.LogWarning(exception, "The Azure SignalR configuration is invalid.");
            return new RealtimeTransportReadinessResult(false, "azure-signalr", "signalr-configuration-invalid");
        }

        if (endpoint is null)
        {
            var isConfigured = !string.IsNullOrWhiteSpace(configuration["AzureSignalR:Endpoint"]) ||
                !string.IsNullOrWhiteSpace(configuration["AzureSignalR:ConnectionString"]);
            return isConfigured
                ? new RealtimeTransportReadinessResult(false, "azure-signalr", "signalr-configuration-invalid")
                : new RealtimeTransportReadinessResult(true, "direct");
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            return (int)response.StatusCode < 500
                ? new RealtimeTransportReadinessResult(true, "azure-signalr")
                : new RealtimeTransportReadinessResult(false, "azure-signalr", "signalr-unavailable");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(exception, "The Azure SignalR readiness probe failed.");
            return new RealtimeTransportReadinessResult(false, "azure-signalr", "signalr-unavailable");
        }
    }

    private static Uri? ResolveEndpoint(IConfiguration configuration)
    {
        var endpoint = configuration["AzureSignalR:Endpoint"];
        if (string.IsNullOrWhiteSpace(endpoint) &&
            configuration["AzureSignalR:ConnectionString"] is { Length: > 0 } connectionString)
        {
            var values = new DbConnectionStringBuilder { ConnectionString = connectionString };
            endpoint = values.TryGetValue("Endpoint", out var value) ? Convert.ToString(value) : null;
        }

        return Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ? uri : null;
    }
}
