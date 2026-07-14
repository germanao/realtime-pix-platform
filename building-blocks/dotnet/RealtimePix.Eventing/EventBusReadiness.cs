using Azure.Messaging.ServiceBus;
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

public sealed class ServiceBusReadinessProbe(
    ServiceBusClient client,
    IOptions<ServiceBusEventBusOptions> options,
    ILogger<ServiceBusReadinessProbe> logger) : CachedEventBusReadinessProbe
{
    protected override async Task<EventBusReadinessResult> CheckCoreAsync(CancellationToken cancellationToken)
    {
        if (client.IsClosed)
        {
            return new EventBusReadinessResult(false, "ServiceBus", "The Service Bus client is closed.");
        }

        try
        {
            var settings = options.Value;
            if (!string.IsNullOrWhiteSpace(settings.QueueName))
            {
                await using var receiver = client.CreateReceiver(settings.QueueName);
                await receiver.PeekMessagesAsync(1, cancellationToken: cancellationToken);
            }
            else if (!string.IsNullOrWhiteSpace(settings.SubscriptionName))
            {
                await using var receiver = client.CreateReceiver(settings.TopicName, settings.SubscriptionName);
                await receiver.PeekMessagesAsync(1, cancellationToken: cancellationToken);
            }
            else
            {
                await using var sender = client.CreateSender(settings.TopicName);
                using var batch = await sender.CreateMessageBatchAsync(cancellationToken);
            }

            return new EventBusReadinessResult(true, "ServiceBus");
        }
        catch (Exception exception) when (exception is ServiceBusException or TimeoutException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "The Service Bus readiness probe failed.");
            return new EventBusReadinessResult(false, "ServiceBus", "service-bus-unavailable");
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
