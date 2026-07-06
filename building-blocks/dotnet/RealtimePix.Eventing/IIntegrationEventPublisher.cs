namespace RealtimePix.Eventing;

public interface IIntegrationEventPublisher
{
    Task PublishAsync<TPayload>(
        string eventType,
        int version,
        string producer,
        TPayload payload,
        string? correlationId = null,
        string? causationId = null,
        CancellationToken cancellationToken = default);
}

