using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace RealtimePix.Eventing;

public sealed class FileIntegrationEventPublisher(
    IOptions<FileEventBusOptions> options,
    ILogger<FileIntegrationEventPublisher> logger) : IIntegrationEventPublisher
{
    private const int MaxAppendAttempts = 5;

    public async Task PublishAsync<TPayload>(
        string eventType,
        int version,
        string producer,
        TPayload payload,
        string? correlationId = null,
        string? causationId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentException.ThrowIfNullOrWhiteSpace(producer);

        var busOptions = options.Value;
        System.IO.Directory.CreateDirectory(busOptions.Directory);

        var envelope = new EventEnvelope(
            Guid.NewGuid(),
            eventType,
            version,
            DateTimeOffset.UtcNow,
            correlationId ?? Guid.NewGuid().ToString("N"),
            causationId,
            producer,
            JsonSerializer.SerializeToElement(payload, JsonDefaults.Options));

        var json = JsonSerializer.Serialize(envelope, JsonDefaults.Options);
        var eventsPath = Path.Combine(busOptions.Directory, "events.jsonl");
        await AppendLineAsync(eventsPath, json, cancellationToken);

        logger.LogInformation("Published integration event {EventType} {EventId}", eventType, envelope.EventId);
    }

    private static async Task AppendLineAsync(string eventsPath, string json, CancellationToken cancellationToken)
    {
        var mutexName = BuildMutexName(eventsPath);
        using var mutex = new Mutex(false, mutexName);

        for (var attempt = 1; attempt <= MaxAppendAttempts; attempt++)
        {
            var hasHandle = false;
            var shouldRetry = false;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                hasHandle = mutex.WaitOne(TimeSpan.FromSeconds(5));
                if (!hasHandle)
                {
                    throw new TimeoutException($"Timed out waiting for local event bus append lock {mutexName}.");
                }

                File.AppendAllText(eventsPath, json + Environment.NewLine);
                return;
            }
            catch (IOException) when (attempt < MaxAppendAttempts)
            {
                shouldRetry = true;
            }
            finally
            {
                if (hasHandle)
                {
                    mutex.ReleaseMutex();
                }
            }

            if (shouldRetry)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25 * attempt), cancellationToken);
            }
        }
    }

    private static string BuildMutexName(string eventsPath)
    {
        var normalizedPath = Path.GetFullPath(eventsPath).ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPath)));
        return $"RealtimePix.EventBus.{hash}";
    }
}
