using Bot.Domain;

namespace Bot.Application;

public sealed record BotAccount(string AccountId, string BankId, decimal Balance);

public interface IBotCatalog
{
    IReadOnlyCollection<BotParticipant> All { get; }
}

public interface IBotWalletClient
{
    Task<bool> IsReadyAsync(CancellationToken cancellationToken);

    Task BootstrapAsync(string userId, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<BotAccount>> GetAccountsAsync(string userId, CancellationToken cancellationToken);

    Task DepositAsync(BotParticipant bot, BotAccount account, decimal amount, CancellationToken cancellationToken);
}

public interface IBotPresencePublisher
{
    Task PublishOnlineAsync(BotParticipant bot, CancellationToken cancellationToken);
}

public sealed record BotReadinessResult(
    bool IsReady,
    bool GatewayReady,
    bool EventBusReady,
    string? Reason = null);

public interface IBotReadinessProbe
{
    Task<BotReadinessResult> CheckAsync(CancellationToken cancellationToken);
}
