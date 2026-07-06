using Azure.Identity;
using Azure.Messaging.ServiceBus;

namespace RealtimePix.Eventing;

public static class ServiceBusClientFactory
{
    public static ServiceBusClient Create(ServiceBusEventBusOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            return new ServiceBusClient(options.ConnectionString);
        }

        if (string.IsNullOrWhiteSpace(options.FullyQualifiedNamespace))
        {
            throw new InvalidOperationException("Service Bus requires EventBus:ServiceBus:ConnectionString or EventBus:ServiceBus:FullyQualifiedNamespace.");
        }

        var credentialOptions = new DefaultAzureCredentialOptions();
        if (!string.IsNullOrWhiteSpace(options.ManagedIdentityClientId))
        {
            credentialOptions.ManagedIdentityClientId = options.ManagedIdentityClientId;
        }

        return new ServiceBusClient(options.FullyQualifiedNamespace, new DefaultAzureCredential(credentialOptions));
    }
}
