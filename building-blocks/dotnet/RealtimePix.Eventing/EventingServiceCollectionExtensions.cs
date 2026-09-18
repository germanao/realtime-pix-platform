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
        services.Configure<EventBusConsumerOptions>(options =>
            options.ConsumerName = configuration["EventBus:ConsumerName"]
                ?? configuration["EventBus:QueueName"]
                ?? consumerName);

        var provider = configuration.GetValue<string>("EventBus:Provider") ?? "File";
        if (provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
        {
            services.Configure<PostgresEventBusOptions>(configuration.GetSection("EventBus"));
            services.PostConfigure<PostgresEventBusOptions>(options => options.ConsumerName = consumerName);
            services.AddSingleton<PostgresEventBus>();
            services.AddSingleton<IIntegrationEventPublisher>(sp => sp.GetRequiredService<PostgresEventBus>());
            services.AddSingleton<IIntegrationMessagePublisher>(sp => sp.GetRequiredService<PostgresEventBus>());
            services.AddSingleton<IIntegrationEnvelopeTransport>(sp => sp.GetRequiredService<PostgresEventBus>());
            services.AddSingleton<IEventBusReadinessProbe>(sp => sp.GetRequiredService<PostgresEventBus>());
            services.AddHostedService<PostgresEventBusWorker>();
            return services;
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

}
