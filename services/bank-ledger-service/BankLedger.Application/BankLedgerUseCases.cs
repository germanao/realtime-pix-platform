using RealtimePix.BankLedger.Domain;
using RealtimePix.Contracts;

namespace RealtimePix.BankLedger.Application;

public sealed class BootstrapAccountHandler(
    IBankLedgerRepository repository,
    IBankEventPublisher publisher,
    ITransactionalExecutor transaction)
{
    public async Task<BootstrapResult> HandleAsync(string userId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        BootstrapResult? result = null;
        await transaction.ExecuteAsync(async innerCancellationToken =>
        {
            result = await repository.BootstrapAsync(userId, innerCancellationToken);
            var context = new MessageContext(userId, userId);
            if (result.AccountCreated)
            {
                await publisher.AccountCreatedAsync(result.Account, context, innerCancellationToken);
            }

            if (result.WelcomeEntry is not null)
            {
                await publisher.FundsDepositedAsync(result.WelcomeEntry, context, innerCancellationToken);
            }
        }, cancellationToken);

        return result ?? throw new InvalidOperationException("Bank account bootstrap produced no result.");
    }
}

public sealed class GetAccountsHandler(IBankLedgerRepository repository)
{
    public Task<IReadOnlyCollection<AccountDto>> HandleAsync(string userId, CancellationToken cancellationToken) =>
        repository.GetAccountsAsync(userId, cancellationToken);
}

public sealed class DepositFundsHandler(
    IBankLedgerRepository repository,
    IBankEventPublisher publisher,
    ITransactionalExecutor transaction)
{
    public async Task<DepositResult?> HandleAsync(
        string accountId,
        string userId,
        decimal amount,
        string reason,
        CancellationToken cancellationToken)
    {
        DepositResult? result = null;
        await transaction.ExecuteAsync(async innerCancellationToken =>
        {
            result = await repository.DepositAsync(
                accountId,
                userId,
                Money.Positive(amount),
                reason,
                innerCancellationToken);
            if (result is not null)
            {
                await publisher.FundsDepositedAsync(
                    result.Entry,
                    new MessageContext(userId, result.Entry.LedgerEntryId),
                    innerCancellationToken);
            }
        }, cancellationToken);

        return result;
    }
}

public sealed class GetLedgerEntriesHandler(IBankLedgerRepository repository)
{
    public Task<IReadOnlyCollection<LedgerEntryDto>?> HandleAsync(
        string accountId,
        string userId,
        CancellationToken cancellationToken) =>
        repository.GetEntriesAsync(accountId, userId, cancellationToken);
}

public sealed class ProcessBankCommandHandler(
    IBankLedgerRepository repository,
    IBankEventPublisher publisher,
    ITransactionalExecutor transaction)
{
    public Task HandleDebitAsync(DebitFundsPayload command, MessageContext context, CancellationToken cancellationToken) =>
        transaction.ExecuteAsync(async innerCancellationToken =>
        {
            var result = await repository.DebitAsync(command, innerCancellationToken);
            if (result.IsDuplicate)
            {
                return;
            }

            if (result.Succeeded)
            {
                await publisher.FundsDebitedAsync(command, result, context, innerCancellationToken);
            }
            else
            {
                await publisher.DebitRejectedAsync(command, result, context, innerCancellationToken);
            }
        }, cancellationToken);

    public Task HandleCreditAsync(CreditFundsPayload command, MessageContext context, CancellationToken cancellationToken) =>
        transaction.ExecuteAsync(async innerCancellationToken =>
        {
            if (command.SimulationMode == SagaSimulationModes.CreditTimeout)
            {
                return;
            }

            var result = await repository.CreditAsync(command, innerCancellationToken);

            if (result.IsDuplicate)
            {
                return;
            }

            if (result.Succeeded)
            {
                await publisher.FundsCreditedAsync(command, result, context, innerCancellationToken);
            }
            else
            {
                await publisher.CreditRejectedAsync(command, result, context, innerCancellationToken);
            }
        }, cancellationToken);

    public Task HandleRefundAsync(RefundFundsPayload command, MessageContext context, CancellationToken cancellationToken) =>
        transaction.ExecuteAsync(async innerCancellationToken =>
        {
            var result = await repository.RefundAsync(command, innerCancellationToken);
            if (result.IsDuplicate)
            {
                return;
            }

            if (result.Succeeded)
            {
                await publisher.FundsRefundedAsync(command, result, context, innerCancellationToken);
            }
            else
            {
                await publisher.RefundRejectedAsync(command, result, context, innerCancellationToken);
            }
        }, cancellationToken);
}
