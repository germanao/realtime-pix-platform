namespace RealtimePix.Eventing;

public interface IIntegrationEventHandler
{
    IReadOnlyCollection<string> EventTypes { get; }

    Task HandleAsync(EventEnvelope envelope, CancellationToken cancellationToken);
}

