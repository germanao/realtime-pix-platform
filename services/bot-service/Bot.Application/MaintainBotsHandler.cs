using Bot.Domain;

namespace Bot.Application;

public sealed class MaintainBotsHandler(
    IBotCatalog catalog,
    IBotWalletClient wallets,
    IBotPresencePublisher presence,
    BotFundingPolicy fundingPolicy)
{
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        foreach (var bot in catalog.All)
        {
            await wallets.BootstrapAsync(bot.UserId, cancellationToken);
            var accounts = await wallets.GetAccountsAsync(bot.UserId, cancellationToken);
            foreach (var account in accounts)
            {
                var topUp = fundingPolicy.CalculateTopUp(account.Balance);
                if (topUp > 0)
                {
                    await wallets.DepositAsync(bot, account, topUp, cancellationToken);
                }
            }

            await presence.PublishOnlineAsync(bot, cancellationToken);
        }
    }
}
