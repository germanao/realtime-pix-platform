using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace RealtimePix.Eventing;

public sealed class FileIntegrationEventPublisher(
    IOptions<FileEventBusOptions> options,
    ILogger<FileIntegrationEventPublisher> logger) :
    IIntegrationEventPublisher,
    IIntegrationMessagePublisher,
    IIntegrationEnvelopeTransport
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
        var envelope = IntegrationMessageFactory.Create(
            eventType,
            version,
            producer,
            payload,
            IntegrationMessageKind.Event,
            IntegrationMessageDestination.Topic("platform-events"),
            subject: null,
            correlationId,
            causationId);

        await PublishEnvelopeAsync(envelope, cancellationToken);
    }

    public async Task PublishCommandAsync<TPayload>(
        string queueName,
        string messageType,
        int version,
        string producer,
        TPayload payload,
        string? subject = null,
        string? correlationId = null,
        string? causationId = null,
        CancellationToken cancellationToken = default)
    {
        var envelope = IntegrationMessageFactory.Create(
            messageType,
            version,
            producer,
            payload,
            IntegrationMessageKind.Command,
            IntegrationMessageDestination.Queue(queueName),
            subject,
            correlationId,
            causationId);

        await PublishEnvelopeAsync(envelope, cancellationToken);
    }

    public async Task PublishEnvelopeAsync(EventEnvelope envelope, CancellationToken cancellationToken = default)
    {
        var busOptions = options.Value;
        System.IO.Directory.CreateDirectory(busOptions.Directory);

        var json = JsonSerializer.Serialize(envelope, JsonDefaults.Options);
        var eventsPath = Path.Combine(busOptions.Directory, "events.jsonl");
        await AppendLineAsync(eventsPath, json, cancellationToken);

        logger.LogInformation(
            "Published local {MessageKind} {EventType} {EventId} to {Destination}",
            envelope.MessageKind,
            envelope.EventType,
            envelope.EventId,
            envelope.Destination);
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
