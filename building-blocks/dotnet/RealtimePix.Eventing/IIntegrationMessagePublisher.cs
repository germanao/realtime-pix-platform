namespace RealtimePix.Eventing;

public enum IntegrationMessageKind
{
    Event,
    Command
}

public enum IntegrationDestinationKind
{
    Topic,
    Queue
}

public sealed record IntegrationMessageDestination(
    IntegrationDestinationKind Kind,
    string Name)
{
    public static IntegrationMessageDestination Topic(string name) =>
        new(IntegrationDestinationKind.Topic, name);

    public static IntegrationMessageDestination Queue(string name) =>
        new(IntegrationDestinationKind.Queue, name);
}

public interface IIntegrationMessagePublisher
{
    Task PublishCommandAsync<TPayload>(
        string queueName,
        string messageType,
        int version,
        string producer,
        TPayload payload,
        string? subject = null,
        string? correlationId = null,
        string? causationId = null,
        CancellationToken cancellationToken = default);
}

public interface IIntegrationEnvelopeTransport
{
    Task PublishEnvelopeAsync(EventEnvelope envelope, CancellationToken cancellationToken = default);
}
