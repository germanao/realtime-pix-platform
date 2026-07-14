namespace RealtimePix.BankLedger.Domain;

public static class LedgerOperationTypes
{
    public const string Welcome = "welcome";
    public const string Deposit = "deposit";
    public const string Debit = "pix-debit";
    public const string Credit = "pix-credit";
    public const string Refund = "pix-refund";
}

public sealed record LedgerOperation(
    string OperationId,
    string AccountId,
    Money Amount,
    decimal BalanceAfter,
    string Direction,
    string OperationType,
    string Description,
    string? TransferId,
    string? CounterpartyUserId,
    DateTimeOffset OccurredAt);

public sealed record LedgerOperationResult(
    bool Succeeded,
    bool IsDuplicate,
    string? Reason,
    decimal Balance,
    LedgerOperation? Operation)
{
    public static LedgerOperationResult Duplicate(decimal balance) =>
        new(true, true, null, balance, null);

    public static LedgerOperationResult Rejected(string reason, decimal balance) =>
        new(false, false, reason, balance, null);

    public static LedgerOperationResult Applied(decimal balance, LedgerOperation operation) =>
        new(true, false, null, balance, operation);
}
