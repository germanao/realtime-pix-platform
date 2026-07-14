using System.Net.Http.Json;
using Bot.Application;
using Bot.Domain;
using RealtimePix.Contracts;
using RealtimePix.Eventing;

namespace Bot.Infrastructure;

public sealed class ContractBotCatalog : IBotCatalog
{
    public IReadOnlyCollection<BotParticipant> All { get; } = KnownBotUsers.All
        .Select(bot => new BotParticipant(bot.UserId, bot.DisplayName))
        .ToArray();
}

public sealed class HttpBotWalletClient(HttpClient client) : IBotWalletClient
{
    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.GetAsync("/health/ready", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    public async Task BootstrapAsync(string userId, CancellationToken cancellationToken)
    {
        using var response = await client.PostAsync(
            $"/wallet/users/{Uri.EscapeDataString(userId)}/bootstrap",
            content: null,
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyCollection<BotAccount>> GetAccountsAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        var accounts = await client.GetFromJsonAsync<AccountResponse[]>(
            $"/wallet/accounts?userId={Uri.EscapeDataString(userId)}",
            cancellationToken);
        return accounts?.Select(account => new BotAccount(account.AccountId, account.BankId, account.Balance)).ToArray() ?? [];
    }

    public async Task DepositAsync(
        BotParticipant bot,
        BotAccount account,
        decimal amount,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync(
            $"/wallet/accounts/{Uri.EscapeDataString(account.AccountId)}/deposit?bankId={Uri.EscapeDataString(account.BankId)}",
            new DepositRequest(bot.UserId, amount, "Bot target balance"),
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private sealed record AccountResponse(string AccountId, string BankId, decimal Balance);

    private sealed record DepositRequest(string UserId, decimal Amount, string? Reason);
}

public sealed class BotPresencePublisher(IIntegrationEventPublisher publisher) : IBotPresencePublisher
{
    public Task PublishOnlineAsync(BotParticipant bot, CancellationToken cancellationToken) =>
        publisher.PublishAsync(
            EventTypes.UserPresenceChanged,
            1,
            BotMetadata.ServiceName,
            new UserPresenceChangedPayload(bot.UserId, bot.DisplayName, true, true, DateTimeOffset.UtcNow),
            correlationId: bot.UserId,
            cancellationToken: cancellationToken);
}

public static class BotMetadata
{
    public const string ServiceName = "bot-service";
}

public sealed class BotReadinessProbe(
    IBotWalletClient gateway,
    IEventBusReadinessProbe eventBus) : IBotReadinessProbe
{
    public async Task<BotReadinessResult> CheckAsync(CancellationToken cancellationToken)
    {
        var gatewayTask = gateway.IsReadyAsync(cancellationToken);
        var eventBusTask = eventBus.CheckAsync(cancellationToken);
        await Task.WhenAll(gatewayTask, eventBusTask);

        var gatewayReady = await gatewayTask;
        var eventBusReady = (await eventBusTask).IsReady;
        return new BotReadinessResult(
            gatewayReady && eventBusReady,
            gatewayReady,
            eventBusReady,
            !gatewayReady ? "gateway-unavailable" : !eventBusReady ? "event-bus-unavailable" : null);
    }
}
