using IdentityPresence.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RealtimePix.Persistence;

namespace IdentityPresence.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddIdentityPresenceInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddSingleton<IPresenceStore, InMemoryPresenceStore>();
            services.AddSingleton<IPresenceTransaction, NoopPresenceTransaction>();
            services.AddScoped<IIdentityDatabaseReadinessProbe, LocalIdentityDatabaseReadinessProbe>();
        }
        else
        {
            services.AddRealtimePixPostgres<IdentityPresenceDbContext>(configuration);
            services.AddScoped<IPresenceStore, EfPresenceStore>();
            services.AddScoped<IPresenceTransaction, EfPresenceTransaction>();
            services.AddScoped<IIdentityDatabaseReadinessProbe, EfIdentityDatabaseReadinessProbe>();
        }

        services.AddHttpClient<IRealtimeTransportReadinessProbe, AzureSignalRReadinessProbe>(client =>
            client.Timeout = TimeSpan.FromSeconds(5));
        services.AddScoped<IIdentityReadinessProbe, IdentityReadinessProbe>();
        services.AddScoped<IPresenceEventPublisher, PresenceEventPublisher>();
        services.AddScoped<JoinAnonymousHandler>();
        services.AddScoped<ConnectAnonymousHandler>();
        services.AddScoped<HeartbeatPresenceHandler>();
        services.AddScoped<LeavePresenceHandler>();
        return services;
    }
}
