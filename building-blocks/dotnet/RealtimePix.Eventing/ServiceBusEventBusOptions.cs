namespace RealtimePix.Eventing;

public sealed class ServiceBusEventBusOptions
{
    public string? ConnectionString { get; set; }

    public string? FullyQualifiedNamespace { get; set; }

    public string TopicName { get; set; } = "platform-events";

    public string? SubscriptionName { get; set; }

    public string? ManagedIdentityClientId { get; set; }

    public int MaxConcurrentCalls { get; set; } = 1;
}
