using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace RealtimePix.Eventing;

public static class EventingServiceCollectionExtensions
{
    public static IServiceCollection AddRealtimePixEventBus(
        this IServiceCollection services,
        IConfiguration configuration,
        string consumerName)
    {
        var provider = configuration.GetValue<string>("EventBus:Provider") ?? "File";
        if (provider.Equals("ServiceBus", StringComparison.OrdinalIgnoreCase))
        {
            return services.AddRealtimePixServiceBusEventBus(configuration, consumerName);
        }

        return services.AddRealtimePixFileEventBus(configuration, consumerName);
    }

    public static IServiceCollection AddRealtimePixFileEventBus(
        this IServiceCollection services,
        IConfiguration configuration,
        string consumerName)
    {
        services.Configure<FileEventBusOptions>(configuration.GetSection("EventBus"));
        services.PostConfigure<FileEventBusOptions>(options =>
        {
            options.ConsumerName = consumerName;
            if (string.IsNullOrWhiteSpace(options.Directory))
            {
                options.Directory = Path.Combine(AppContext.BaseDirectory, "local-bus");
            }
        });
        services.AddSingleton<IIntegrationEventPublisher, FileIntegrationEventPublisher>();
        services.AddSingleton<IIntegrationMessagePublisher>(serviceProvider =>
            (FileIntegrationEventPublisher)serviceProvider.GetRequiredService<IIntegrationEventPublisher>());
        services.AddSingleton<IIntegrationEnvelopeTransport>(serviceProvider =>
            (FileIntegrationEventPublisher)serviceProvider.GetRequiredService<IIntegrationEventPublisher>());
        services.AddSingleton<IEventBusReadinessProbe, FileEventBusReadinessProbe>();
        services.AddHostedService<FileEventBusWorker>();
        return services;
    }

    private static IServiceCollection AddRealtimePixServiceBusEventBus(
        this IServiceCollection services,
        IConfiguration configuration,
        string consumerName)
    {
        services.Configure<ServiceBusEventBusOptions>(configuration.GetSection("EventBus:ServiceBus"));
        services.PostConfigure<ServiceBusEventBusOptions>(options =>
        {
            options.SubscriptionName = string.IsNullOrWhiteSpace(options.SubscriptionName)
                ? ResolveSubscriptionName(consumerName)
                : options.SubscriptionName;
        });

        services.AddSingleton(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<ServiceBusEventBusOptions>>().Value;
            return ServiceBusClientFactory.Create(options);
        });
        services.AddSingleton<ServiceBusIntegrationEventPublisher>();
        services.AddSingleton<IIntegrationEventPublisher>(serviceProvider =>
            serviceProvider.GetRequiredService<ServiceBusIntegrationEventPublisher>());
        services.AddSingleton<IIntegrationMessagePublisher>(serviceProvider =>
            serviceProvider.GetRequiredService<ServiceBusIntegrationEventPublisher>());
        services.AddSingleton<IIntegrationEnvelopeTransport>(serviceProvider =>
            serviceProvider.GetRequiredService<ServiceBusIntegrationEventPublisher>());
        services.AddSingleton<IEventBusReadinessProbe, ServiceBusReadinessProbe>();

        if (!string.IsNullOrWhiteSpace(ResolveSubscriptionName(consumerName)) ||
            !string.IsNullOrWhiteSpace(configuration["EventBus:ServiceBus:QueueName"]))
        {
            services.AddHostedService<ServiceBusEventBusWorker>();
        }

        return services;
    }

    private static string? ResolveSubscriptionName(string consumerName)
    {
        return consumerName switch
        {
            "wallet-ledger-service" => "wallet-ledger",
            "transaction-service" => "transaction",
            "realtime-events-service" => "realtime-events",
            _ => null
        };
    }
}
