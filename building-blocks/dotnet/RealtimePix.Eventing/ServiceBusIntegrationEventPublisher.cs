using System.Collections.Concurrent;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace RealtimePix.Eventing;

public sealed class ServiceBusIntegrationEventPublisher(
    ServiceBusClient client,
    IOptions<ServiceBusEventBusOptions> options,
    ILogger<ServiceBusIntegrationEventPublisher> logger) :
    IIntegrationEventPublisher,
    IIntegrationMessagePublisher,
    IIntegrationEnvelopeTransport,
    IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, ServiceBusSender> _senders = new(StringComparer.OrdinalIgnoreCase);

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
            IntegrationMessageDestination.Topic(options.Value.TopicName),
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
        message.ApplicationProperties["messageKind"] = envelope.MessageKind;
        message.ApplicationProperties["destinationKind"] = envelope.DestinationKind;
        if (!string.IsNullOrWhiteSpace(envelope.CausationId))
        {
            message.ApplicationProperties["causationId"] = envelope.CausationId;
        }

        var destination = envelope.Destination ?? options.Value.TopicName;
        var sender = _senders.GetOrAdd(destination, client.CreateSender);
        await sender.SendMessageAsync(message, cancellationToken);
        logger.LogInformation(
            "Published Service Bus {MessageKind} {EventType} {EventId} to {Destination}",
            envelope.MessageKind,
            envelope.EventType,
            envelope.EventId,
            destination);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var sender in _senders.Values)
        {
            await sender.DisposeAsync();
        }
    }
}
