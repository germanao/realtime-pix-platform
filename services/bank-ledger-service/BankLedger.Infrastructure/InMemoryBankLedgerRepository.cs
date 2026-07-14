using RealtimePix.BankLedger.Application;
using RealtimePix.BankLedger.Domain;
using RealtimePix.Contracts;

namespace RealtimePix.BankLedger.Infrastructure;

public sealed class InMemoryBankLedgerRepository(BankDescriptor bank) : IBankLedgerRepository
{
    private readonly object _gate = new();
    private readonly Dictionary<string, AccountState> _accounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<LedgerEntryDto>> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string TransferId, string OperationType), LedgerOperationResult> _operations = new();

    public Task<BootstrapResult> BootstrapAsync(string userId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var accountId = EfBankLedgerRepository.BuildAccountId(userId, bank.BankId);
            if (_accounts.TryGetValue(accountId, out var existing))
            {
                return Task.FromResult(new BootstrapResult(ToAccount(existing), false, false, null));
            }

            var account = new AccountState(accountId, userId, bank.BankId, bank.BankName, bank.WelcomeBalance);
            _accounts.Add(accountId, account);
            _entries.Add(accountId, []);

            LedgerEntryDto? welcomeEntry = null;
            if (bank.WelcomeBalance > 0)
            {
                welcomeEntry = AddEntry(
                    account,
                    bank.WelcomeBalance,
                    "credit",
                    LedgerOperationTypes.Welcome,
                    "Welcome balance",
                    transferId: null,
                    counterpartyUserId: null);
            }

            return Task.FromResult(new BootstrapResult(ToAccount(account), true, welcomeEntry is not null, welcomeEntry));
        }
    }

    public Task<IReadOnlyCollection<AccountDto>> GetAccountsAsync(string userId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyCollection<AccountDto> result = _accounts.Values
                .Where(item => item.UserId.Equals(userId, StringComparison.OrdinalIgnoreCase))
                .Select(ToAccount)
                .ToArray();
            return Task.FromResult(result);
        }
    }

    public Task<DepositResult?> DepositAsync(
        string accountId,
        string userId,
        Money amount,
        string reason,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!TryGetOwnedAccount(accountId, userId, out var account))
            {
                return Task.FromResult<DepositResult?>(null);
            }

            account.Balance += amount.Amount;
            var entry = AddEntry(account, amount.Amount, "credit", LedgerOperationTypes.Deposit, reason, null, null);
            return Task.FromResult<DepositResult?>(new DepositResult(entry, account.Balance));
        }
    }

    public Task<IReadOnlyCollection<LedgerEntryDto>?> GetEntriesAsync(
        string accountId,
        string userId,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!TryGetOwnedAccount(accountId, userId, out _))
            {
                return Task.FromResult<IReadOnlyCollection<LedgerEntryDto>?>(null);
            }

            IReadOnlyCollection<LedgerEntryDto> result = _entries[accountId]
                .OrderByDescending(item => item.OccurredAt)
                .ToArray();
            return Task.FromResult<IReadOnlyCollection<LedgerEntryDto>?>(result);
        }
    }

    public Task<LedgerOperationResult> DebitAsync(DebitFundsPayload command, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            EnsureBank(command.SenderBankId);
            var amount = Money.Positive(command.Amount).Amount;
            if (TryGetOperation(command.TransferId, LedgerOperationTypes.Debit, out var duplicate))
            {
                return Task.FromResult(duplicate);
            }

            if (!TryGetOwnedAccount(command.SenderAccountId, command.SenderUserId, out var account))
            {
                return Task.FromResult(StoreRejected(command.TransferId, LedgerOperationTypes.Debit, "Sender account was not found.", 0m));
            }

            if (account.Balance < amount)
            {
                return Task.FromResult(StoreRejected(command.TransferId, LedgerOperationTypes.Debit, "Insufficient fictional funds.", account.Balance));
            }

            account.Balance -= amount;
            var entry = AddEntry(
                account,
                -amount,
                "debit",
                LedgerOperationTypes.Debit,
                $"PIX debit {command.TransferId}",
                command.TransferId,
                command.RecipientUserId);
            return Task.FromResult(StoreApplied(command.TransferId, LedgerOperationTypes.Debit, account.Balance, entry));
        }
    }

    public Task<LedgerOperationResult> CreditAsync(CreditFundsPayload command, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            EnsureBank(command.RecipientBankId);
            var amount = Money.Positive(command.Amount).Amount;
            if (TryGetOperation(command.TransferId, LedgerOperationTypes.Credit, out var duplicate))
            {
                return Task.FromResult(duplicate);
            }

            if (command.SimulationMode is SagaSimulationModes.CreditRejected or SagaSimulationModes.RefundRejectedTest)
            {
                return Task.FromResult(StoreRejected(
                    command.TransferId,
                    LedgerOperationTypes.Credit,
                    "Credit rejected by the educational failure simulation.",
                    0m));
            }

            return Task.FromResult(ApplyCredit(
                command.TransferId,
                LedgerOperationTypes.Credit,
                command.RecipientAccountId,
                command.RecipientUserId,
                amount,
                command.SenderUserId,
                "PIX credit"));
        }
    }

    public Task<LedgerOperationResult> RefundAsync(RefundFundsPayload command, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            EnsureBank(command.SenderBankId);
            var amount = Money.Positive(command.Amount).Amount;
            if (TryGetOperation(command.TransferId, LedgerOperationTypes.Refund, out var duplicate))
            {
                return Task.FromResult(duplicate);
            }

            if (command.SimulationMode == SagaSimulationModes.RefundRejectedTest)
            {
                return Task.FromResult(StoreRejected(
                    command.TransferId,
                    LedgerOperationTypes.Refund,
                    "Refund rejected by the automated manual-intervention simulation.",
                    0m));
            }

            return Task.FromResult(ApplyCredit(
                command.TransferId,
                LedgerOperationTypes.Refund,
                command.SenderAccountId,
                command.SenderUserId,
                amount,
                counterpartyUserId: null,
                description: "PIX compensation refund"));
        }
    }

    private LedgerOperationResult ApplyCredit(
        string transferId,
        string operationType,
        string accountId,
        string userId,
        decimal amount,
        string? counterpartyUserId,
        string description)
    {
        if (!TryGetOwnedAccount(accountId, userId, out var account))
        {
            return StoreRejected(transferId, operationType, "Account was not found.", 0m);
        }

        account.Balance += amount;
        var entry = AddEntry(
            account,
            amount,
            "credit",
            operationType,
            $"{description} {transferId}",
            transferId,
            counterpartyUserId);
        return StoreApplied(transferId, operationType, account.Balance, entry);
    }

    private LedgerOperationResult StoreApplied(
        string transferId,
        string operationType,
        decimal balance,
        LedgerEntryDto entry)
    {
        var operation = new LedgerOperation(
            entry.LedgerEntryId,
            entry.AccountId,
            new Money(entry.Amount),
            entry.BalanceAfter,
            entry.Direction,
            entry.EntryType,
            entry.Description,
            entry.TransferId,
            entry.CounterpartyUserId,
            entry.OccurredAt);
        var result = LedgerOperationResult.Applied(balance, operation);
        _operations.Add((transferId, operationType), result);
        return result;
    }

    private LedgerOperationResult StoreRejected(string transferId, string operationType, string reason, decimal balance)
    {
        var result = LedgerOperationResult.Rejected(reason, balance);
        _operations.Add((transferId, operationType), result);
        return result;
    }

    private bool TryGetOperation(string transferId, string operationType, out LedgerOperationResult result)
    {
        if (_operations.TryGetValue((transferId, operationType), out var stored))
        {
            result = stored with { IsDuplicate = true, Operation = null };
            return true;
        }

        result = default!;
        return false;
    }

    private bool TryGetOwnedAccount(string accountId, string userId, out AccountState account)
    {
        if (_accounts.TryGetValue(accountId, out var found) &&
            found.UserId.Equals(userId, StringComparison.OrdinalIgnoreCase) &&
            found.BankId.Equals(bank.BankId, StringComparison.OrdinalIgnoreCase))
        {
            account = found;
            return true;
        }

        account = default!;
        return false;
    }

    private LedgerEntryDto AddEntry(
        AccountState account,
        decimal amount,
        string direction,
        string operationType,
        string description,
        string? transferId,
        string? counterpartyUserId)
    {
        var entry = new LedgerEntryDto(
            Guid.NewGuid().ToString("N"),
            account.AccountId,
            account.UserId,
            account.BankId,
            amount,
            account.Balance,
            direction,
            description,
            DateTimeOffset.UtcNow,
            operationType,
            transferId,
            counterpartyUserId);
        _entries[account.AccountId].Add(entry);
        return entry;
    }

    private void EnsureBank(string bankId)
    {
        if (!bank.BankId.Equals(bankId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Command for bank '{bankId}' reached '{bank.BankId}'.");
        }
    }

    private static AccountDto ToAccount(AccountState account) =>
        new(account.AccountId, account.UserId, account.BankId, account.BankName, account.Balance);

    private sealed class AccountState(
        string accountId,
        string userId,
        string bankId,
        string bankName,
        decimal balance)
    {
        public string AccountId { get; } = accountId;
        public string UserId { get; } = userId;
        public string BankId { get; } = bankId;
        public string BankName { get; } = bankName;
        public decimal Balance { get; set; } = balance;
    }
}
