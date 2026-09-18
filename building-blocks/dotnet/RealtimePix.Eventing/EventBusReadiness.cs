using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace RealtimePix.Eventing;

public sealed record EventBusReadinessResult(bool IsReady, string Provider, string? Reason = null);

public interface IEventBusReadinessProbe
{
    Task<EventBusReadinessResult> CheckAsync(CancellationToken cancellationToken);
}

public sealed class FileEventBusReadinessProbe(
    IOptions<FileEventBusOptions> options,
    ILogger<FileEventBusReadinessProbe> logger)
    : CachedEventBusReadinessProbe
{
    protected override async Task<EventBusReadinessResult> CheckCoreAsync(CancellationToken cancellationToken)
    {
        var directory = options.Value.Directory;
        try
        {
            Directory.CreateDirectory(directory);
            var probePath = Path.Combine(directory, $".readiness-{Guid.NewGuid():N}");
            await File.WriteAllTextAsync(probePath, "ready", cancellationToken);
            File.Delete(probePath);
            return new EventBusReadinessResult(true, "File");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "The local event transport readiness probe failed.");
            return new EventBusReadinessResult(false, "File", "file-transport-unavailable");
        }
    }
}

public abstract class CachedEventBusReadinessProbe : IEventBusReadinessProbe
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private EventBusReadinessResult? _cached;
    private DateTimeOffset _cachedAt;

    public async Task<EventBusReadinessResult> CheckAsync(CancellationToken cancellationToken)
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

    protected abstract Task<EventBusReadinessResult> CheckCoreAsync(CancellationToken cancellationToken);
}
