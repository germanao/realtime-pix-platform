using Microsoft.Extensions.DependencyInjection;
using RealtimePix.ApiGateway.Application;

namespace RealtimePix.ApiGateway.Infrastructure;

public static class ApiGatewayServiceCollectionExtensions
{
    public static IServiceCollection AddGatewayInfrastructure(this IServiceCollection services)
    {
        services.AddHttpClient<IBackendClient, ConfiguredBackendClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddScoped<ProxyRequestHandler>();
        services.AddScoped<WalletGatewayHandler>();
        return services;
    }
}
