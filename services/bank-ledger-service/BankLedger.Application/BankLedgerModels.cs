using RealtimePix.BankLedger.Domain;

namespace RealtimePix.BankLedger.Application;

public sealed record BankDescriptor(string BankId, string BankName, decimal WelcomeBalance);

public sealed record AccountDto(
    string AccountId,
    string UserId,
    string BankId,
    string BankName,
    decimal Balance);

public sealed record LedgerEntryDto(
    string LedgerEntryId,
    string AccountId,
    string UserId,
    string BankId,
    decimal Amount,
    decimal BalanceAfter,
    string Direction,
    string Description,
    DateTimeOffset OccurredAt,
    string EntryType,
    string? TransferId,
    string? CounterpartyUserId);

public sealed record BootstrapResult(
    AccountDto Account,
    bool AccountCreated,
    bool WelcomeCreditApplied,
    LedgerEntryDto? WelcomeEntry);

public sealed record DepositResult(LedgerEntryDto Entry, decimal NewBalance);

public interface IBankLedgerRepository
{
    Task<BootstrapResult> BootstrapAsync(string userId, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<AccountDto>> GetAccountsAsync(string userId, CancellationToken cancellationToken);

    Task<DepositResult?> DepositAsync(string accountId, string userId, Money amount, string reason, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<LedgerEntryDto>?> GetEntriesAsync(string accountId, string userId, CancellationToken cancellationToken);

    Task<LedgerOperationResult> DebitAsync(RealtimePix.Contracts.DebitFundsPayload command, CancellationToken cancellationToken);

    Task<LedgerOperationResult> CreditAsync(RealtimePix.Contracts.CreditFundsPayload command, CancellationToken cancellationToken);

    Task<LedgerOperationResult> RefundAsync(RealtimePix.Contracts.RefundFundsPayload command, CancellationToken cancellationToken);
}

public interface IBankEventPublisher
{
    Task AccountCreatedAsync(AccountDto account, MessageContext context, CancellationToken cancellationToken);

    Task FundsDepositedAsync(LedgerEntryDto entry, MessageContext context, CancellationToken cancellationToken);

    Task FundsDebitedAsync(RealtimePix.Contracts.DebitFundsPayload command, LedgerOperationResult result, MessageContext context, CancellationToken cancellationToken);

    Task DebitRejectedAsync(RealtimePix.Contracts.DebitFundsPayload command, LedgerOperationResult result, MessageContext context, CancellationToken cancellationToken);

    Task FundsCreditedAsync(RealtimePix.Contracts.CreditFundsPayload command, LedgerOperationResult result, MessageContext context, CancellationToken cancellationToken);

    Task CreditRejectedAsync(RealtimePix.Contracts.CreditFundsPayload command, LedgerOperationResult result, MessageContext context, CancellationToken cancellationToken);

    Task FundsRefundedAsync(RealtimePix.Contracts.RefundFundsPayload command, LedgerOperationResult result, MessageContext context, CancellationToken cancellationToken);

    Task RefundRejectedAsync(RealtimePix.Contracts.RefundFundsPayload command, LedgerOperationResult result, MessageContext context, CancellationToken cancellationToken);
}

public sealed record MessageContext(string CorrelationId, string CausationId);

public interface ITransactionalExecutor
{
    Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken);
}

public sealed record ReadinessResult(bool IsReady, string? Reason = null);

public interface IBankReadinessProbe
{
    Task<ReadinessResult> CheckAsync(CancellationToken cancellationToken);
}
