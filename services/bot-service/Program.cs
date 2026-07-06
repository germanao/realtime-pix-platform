using System.Net.Http.Json;
using RealtimePix.Contracts;
using RealtimePix.Eventing;

const string ServiceName = "bot-service";

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRealtimePixEventBus(builder.Configuration, ServiceName);
builder.Services.AddHostedService<BotHeartbeatWorker>();

var app = builder.Build();
app.MapGet("/health", () => Results.Ok(new { service = ServiceName, status = "ok" }));
app.MapGet("/health/live", () => Results.Ok(new { service = ServiceName, status = "live" }));
app.MapGet("/health/ready", () => Results.Ok(new { service = ServiceName, status = "ready" }));
app.Run();

public sealed class BotHeartbeatWorker(
    IIntegrationEventPublisher publisher,
    IConfiguration configuration,
    ILogger<BotHeartbeatWorker> logger) : BackgroundService
{
    private static readonly HttpClient HttpClient = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await TrySeedBotWalletsAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var bot in KnownBotUsers.All)
            {
                await publisher.PublishAsync(
                    EventTypes.UserPresenceChanged,
                    1,
                    BotServiceMetadata.Name,
                    new UserPresenceChangedPayload(bot.UserId, bot.DisplayName, true, true, DateTimeOffset.UtcNow),
                    correlationId: bot.UserId,
                    cancellationToken: stoppingToken);
            }

            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
    }

    private async Task TrySeedBotWalletsAsync(CancellationToken cancellationToken)
    {
        var walletUrl = configuration["WalletServiceUrl"];
        if (string.IsNullOrWhiteSpace(walletUrl))
        {
            return;
        }

        foreach (var bot in KnownBotUsers.All)
        {
            try
            {
                var accounts = await HttpClient.GetFromJsonAsync<AccountResponse[]>($"{walletUrl}/wallet/accounts?userId={Uri.EscapeDataString(bot.UserId)}", cancellationToken);
                foreach (var account in accounts ?? [])
                {
                    await HttpClient.PostAsJsonAsync(
                        $"{walletUrl}/wallet/accounts/{Uri.EscapeDataString(account.AccountId)}/deposit",
                        new DepositRequest(bot.UserId, 250m, "Bot opening balance"),
                        cancellationToken);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Bot wallet seeding skipped because wallet service is not available yet.");
            }
        }
    }

    private sealed record AccountResponse(string AccountId, string UserId, string BankName, decimal Balance);

    private sealed record DepositRequest(string UserId, decimal Amount, string? Reason);
}

public static class BotServiceMetadata
{
    public const string Name = "bot-service";
}
