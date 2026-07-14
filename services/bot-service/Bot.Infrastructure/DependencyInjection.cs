using Bot.Application;
using Bot.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bot.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddBotInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var walletServiceUrl = configuration["WalletServiceUrl"]
            ?? throw new InvalidOperationException("WalletServiceUrl must be configured.");

        services.AddSingleton<IBotCatalog, ContractBotCatalog>();
        services.AddSingleton(new BotFundingPolicy(configuration.GetValue("Bot:TargetBalance", 1_000m)));
        services.AddHttpClient<IBotWalletClient, HttpBotWalletClient>(client =>
        {
            client.BaseAddress = new Uri(walletServiceUrl, UriKind.Absolute);
            client.Timeout = TimeSpan.FromSeconds(15);
        });
        services.AddScoped<IBotPresencePublisher, BotPresencePublisher>();
        services.AddScoped<IBotReadinessProbe, BotReadinessProbe>();
        services.AddScoped<MaintainBotsHandler>();
        return services;
    }
}
