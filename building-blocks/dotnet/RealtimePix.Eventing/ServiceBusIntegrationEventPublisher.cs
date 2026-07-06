using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace RealtimePix.Eventing;

public sealed class ServiceBusIntegrationEventPublisher(
    ServiceBusClient client,
    IOptions<ServiceBusEventBusOptions> options,
    ILogger<ServiceBusIntegrationEventPublisher> logger) : IIntegrationEventPublisher, IAsyncDisposable
{
    private readonly ServiceBusSender _sender = client.CreateSender(options.Value.TopicName);

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
        var message = new ServiceBusMessage(BinaryData.FromString(json))
        {
            MessageId = envelope.EventId.ToString("N"),
            CorrelationId = envelope.CorrelationId,
            Subject = envelope.EventType,
            ContentType = "application/json"
        };

        message.ApplicationProperties["eventType"] = envelope.EventType;
        message.ApplicationProperties["version"] = envelope.Version;
        message.ApplicationProperties["producer"] = envelope.Producer;
        if (!string.IsNullOrWhiteSpace(envelope.CausationId))
        {
            message.ApplicationProperties["causationId"] = envelope.CausationId;
        }

        await _sender.SendMessageAsync(message, cancellationToken);
        logger.LogInformation("Published Service Bus integration event {EventType} {EventId}", eventType, envelope.EventId);
    }

    public async ValueTask DisposeAsync()
    {
        await _sender.DisposeAsync();
    }
}
