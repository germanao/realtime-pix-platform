using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RealtimeEvents.Application;
using RealtimePix.Eventing;
using RealtimePix.Persistence;

namespace RealtimeEvents.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddRealtimeEventsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddSingleton<IRealtimeProjectionStore, InMemoryRealtimeProjectionStore>();
            services.AddSingleton<IRealtimeProjectionTransaction, NoopRealtimeProjectionTransaction>();
            services.AddScoped<IRealtimeDatabaseReadinessProbe, LocalRealtimeDatabaseReadinessProbe>();
        }
        else
        {
            services.AddRealtimePixPostgres<RealtimeProjectionDbContext>(configuration);
            services.AddScoped<IRealtimeProjectionStore, EfRealtimeProjectionStore>();
            services.AddScoped<IRealtimeProjectionTransaction, EfRealtimeProjectionTransaction>();
            services.AddScoped<IRealtimeDatabaseReadinessProbe, EfRealtimeDatabaseReadinessProbe>();
        }

        services.AddHttpClient<IRealtimeTransportReadinessProbe, AzureSignalRReadinessProbe>(client =>
            client.Timeout = TimeSpan.FromSeconds(5));
        services.AddScoped<IRealtimeEventsReadinessProbe, RealtimeEventsReadinessProbe>();
        services.AddScoped<ProjectPlatformEventHandler>();
        services.AddScoped<IRealtimeProjectionNotifier, SignalRProjectionNotifier>();
        services.AddScoped<IArchitectureFlowPublisher, ArchitectureFlowPublisher>();
        services.AddScoped<IIntegrationEventHandler, PlatformEventProjectionAdapter>();
        return services;
    }
}
