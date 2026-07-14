using Microsoft.EntityFrameworkCore;
using RealtimePix.BankLedger.Application;
using RealtimePix.BankLedger.Domain;
using RealtimePix.Contracts;

namespace RealtimePix.BankLedger.Infrastructure;

public sealed class EfBankLedgerRepository(
    BankLedgerDbContext dbContext,
    BankDescriptor bank) : IBankLedgerRepository
{
    public async Task<BootstrapResult> BootstrapAsync(string userId, CancellationToken cancellationToken)
    {
        var existing = await dbContext.Accounts.SingleOrDefaultAsync(
            item => item.UserId == userId && item.BankId == bank.BankId,
            cancellationToken);
        if (existing is not null)
        {
            return new BootstrapResult(ToAccount(existing), false, false, null);
        }

        var now = DateTimeOffset.UtcNow;
        var account = new BankAccountEntity
        {
            AccountId = BuildAccountId(userId, bank.BankId),
            UserId = userId,
            BankId = bank.BankId,
            BankName = bank.BankName,
            Balance = bank.WelcomeBalance,
            CreatedAt = now,
            UpdatedAt = now
        };
        dbContext.Accounts.Add(account);

        BankLedgerEntryEntity? welcome = null;
        if (bank.WelcomeBalance > 0)
        {
            welcome = CreateEntry(
                account,
                bank.WelcomeBalance,
                "credit",
                LedgerOperationTypes.Welcome,
                "Welcome balance",
                transferId: null,
                counterpartyUserId: null,
                now);
            dbContext.LedgerEntries.Add(welcome);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return new BootstrapResult(ToAccount(account), true, welcome is not null, welcome is null ? null : ToLedgerEntry(welcome));
    }

    public async Task<IReadOnlyCollection<AccountDto>> GetAccountsAsync(string userId, CancellationToken cancellationToken)
    {
        return await dbContext.Accounts.AsNoTracking()
            .Where(item => item.UserId == userId && item.BankId == bank.BankId)
            .Select(item => new AccountDto(item.AccountId, item.UserId, item.BankId, item.BankName, item.Balance))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<DepositResult?> DepositAsync(
        string accountId,
        string userId,
        Money amount,
        string reason,
        CancellationToken cancellationToken)
    {
        var account = await LoadAccountForUpdateAsync(accountId, userId, cancellationToken);
        if (account is null)
        {
            return null;
        }

        account.Balance += amount.Amount;
        account.UpdatedAt = DateTimeOffset.UtcNow;
        var entry = CreateEntry(account, amount.Amount, "credit", LedgerOperationTypes.Deposit, reason, null, null, account.UpdatedAt);
        dbContext.LedgerEntries.Add(entry);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new DepositResult(ToLedgerEntry(entry), account.Balance);
    }

    public async Task<IReadOnlyCollection<LedgerEntryDto>?> GetEntriesAsync(
        string accountId,
        string userId,
        CancellationToken cancellationToken)
    {
        var ownsAccount = await dbContext.Accounts.AsNoTracking()
            .AnyAsync(item => item.AccountId == accountId && item.UserId == userId && item.BankId == bank.BankId, cancellationToken);
        if (!ownsAccount)
        {
            return null;
        }

        return await dbContext.LedgerEntries.AsNoTracking()
            .Where(item => item.AccountId == accountId)
            .OrderByDescending(item => item.OccurredAt)
            .Select(item => new LedgerEntryDto(
                item.LedgerEntryId,
                item.AccountId,
                item.UserId,
                item.BankId,
                item.Amount,
                item.BalanceAfter,
                item.Direction,
                item.Description,
                item.OccurredAt,
                item.OperationType,
                item.TransferId,
                item.CounterpartyUserId))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<LedgerOperationResult> DebitAsync(DebitFundsPayload command, CancellationToken cancellationToken)
    {
        EnsureBank(command.SenderBankId);
        var amount = Money.Positive(command.Amount).Amount;
        var duplicate = await ClaimOperationAsync(
            command.TransferId,
            LedgerOperationTypes.Debit,
            command.SenderAccountId,
            cancellationToken);
        if (duplicate is not null)
        {
            return duplicate;
        }

        var now = DateTimeOffset.UtcNow;
        var updated = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE bank_accounts
            SET "Balance" = "Balance" - {amount}, "UpdatedAt" = {now}
            WHERE "AccountId" = {command.SenderAccountId}
              AND "UserId" = {command.SenderUserId}
              AND "BankId" = {bank.BankId}
              AND "Balance" >= {amount}
            """,
            cancellationToken);

        var account = await dbContext.Accounts.SingleOrDefaultAsync(
            item => item.AccountId == command.SenderAccountId && item.UserId == command.SenderUserId,
            cancellationToken);
        if (updated == 0)
        {
            var reason = account is null ? "Sender account was not found." : "Insufficient fictional funds.";
            await CompleteProcessedAsync(command.TransferId, LedgerOperationTypes.Debit, "rejected", reason, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            return LedgerOperationResult.Rejected(reason, account?.Balance ?? 0m);
        }

        var debitedAccount = account ?? throw new InvalidOperationException("The debited account disappeared after a successful update.");
        await dbContext.Entry(debitedAccount).ReloadAsync(cancellationToken);
        var entry = CreateEntry(
            debitedAccount,
            -amount,
            "debit",
            LedgerOperationTypes.Debit,
            $"PIX debit {command.TransferId}",
            command.TransferId,
            command.RecipientUserId,
            now);
        dbContext.LedgerEntries.Add(entry);
        await CompleteProcessedAsync(command.TransferId, LedgerOperationTypes.Debit, "succeeded", null, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return LedgerOperationResult.Applied(debitedAccount.Balance, ToOperation(entry));
    }

    public async Task<LedgerOperationResult> CreditAsync(CreditFundsPayload command, CancellationToken cancellationToken)
    {
        EnsureBank(command.RecipientBankId);
        var amount = Money.Positive(command.Amount).Amount;
        var duplicate = await ClaimOperationAsync(
            command.TransferId,
            LedgerOperationTypes.Credit,
            command.RecipientAccountId,
            cancellationToken);
        if (duplicate is not null)
        {
            return duplicate;
        }

        if (command.SimulationMode is SagaSimulationModes.CreditRejected or SagaSimulationModes.RefundRejectedTest)
        {
            const string reason = "Credit rejected by the educational failure simulation.";
            await CompleteProcessedAsync(command.TransferId, LedgerOperationTypes.Credit, "rejected", reason, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            return LedgerOperationResult.Rejected(reason, 0m);
        }

        return await ApplyClaimedCreditAsync(
            command.TransferId,
            LedgerOperationTypes.Credit,
            command.RecipientAccountId,
            command.RecipientUserId,
            amount,
            command.SenderUserId,
            "PIX credit",
            cancellationToken);
    }

    public async Task<LedgerOperationResult> RefundAsync(RefundFundsPayload command, CancellationToken cancellationToken)
    {
        EnsureBank(command.SenderBankId);
        var amount = Money.Positive(command.Amount).Amount;
        var duplicate = await ClaimOperationAsync(
            command.TransferId,
            LedgerOperationTypes.Refund,
            command.SenderAccountId,
            cancellationToken);
        if (duplicate is not null)
        {
            return duplicate;
        }

        if (command.SimulationMode == SagaSimulationModes.RefundRejectedTest)
        {
            const string reason = "Refund rejected by the automated manual-intervention simulation.";
            await CompleteProcessedAsync(command.TransferId, LedgerOperationTypes.Refund, "rejected", reason, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            return LedgerOperationResult.Rejected(reason, 0m);
        }

        return await ApplyClaimedCreditAsync(
            command.TransferId,
            LedgerOperationTypes.Refund,
            command.SenderAccountId,
            command.SenderUserId,
            amount,
            counterpartyUserId: null,
            description: "PIX compensation refund",
            cancellationToken: cancellationToken);
    }

    private async Task<LedgerOperationResult> ApplyClaimedCreditAsync(
        string transferId,
        string operationType,
        string accountId,
        string userId,
        decimal amount,
        string? counterpartyUserId,
        string description,
        CancellationToken cancellationToken)
    {
        var account = await LoadAccountForUpdateAsync(accountId, userId, cancellationToken);
        if (account is null)
        {
            const string reason = "Account was not found.";
            await CompleteProcessedAsync(transferId, operationType, "rejected", reason, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            return LedgerOperationResult.Rejected(reason, 0m);
        }

        account.Balance += amount;
        account.UpdatedAt = DateTimeOffset.UtcNow;
        var entry = CreateEntry(account, amount, "credit", operationType, $"{description} {transferId}", transferId, counterpartyUserId, account.UpdatedAt);
        dbContext.LedgerEntries.Add(entry);
        await CompleteProcessedAsync(transferId, operationType, "succeeded", null, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return LedgerOperationResult.Applied(account.Balance, ToOperation(entry));
    }

    private async Task<LedgerOperationResult?> ClaimOperationAsync(
        string transferId,
        string operationType,
        string accountId,
        CancellationToken cancellationToken)
    {
        if (dbContext.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
        {
            if (dbContext.Database.CurrentTransaction is null)
            {
                throw new InvalidOperationException("Bank ledger commands require an explicit database transaction.");
            }

            const string processingOutcome = "processing";
            var inserted = await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO processed_bank_operations ("TransferId", "OperationType", "Outcome", "ProcessedAt")
                VALUES ({transferId}, {operationType}, {processingOutcome}, {DateTimeOffset.UtcNow})
                ON CONFLICT ("TransferId", "OperationType") DO NOTHING
                """,
                cancellationToken);

            return inserted == 0
                ? await FindDuplicateAsync(transferId, operationType, accountId, cancellationToken)
                : null;
        }

        var duplicate = await FindDuplicateAsync(transferId, operationType, accountId, cancellationToken);
        if (duplicate is null)
        {
            RecordProcessed(transferId, operationType, "processing", null);
        }

        return duplicate;
    }

    private async Task CompleteProcessedAsync(
        string transferId,
        string operationType,
        string outcome,
        string? reason,
        CancellationToken cancellationToken)
    {
        var processed = dbContext.ProcessedOperations.Local.SingleOrDefault(
            item => item.TransferId == transferId && item.OperationType == operationType)
            ?? await dbContext.ProcessedOperations.SingleAsync(
                item => item.TransferId == transferId && item.OperationType == operationType,
                cancellationToken);
        processed.Outcome = outcome;
        processed.Reason = reason;
        processed.ProcessedAt = DateTimeOffset.UtcNow;
    }

    private async Task<BankAccountEntity?> LoadAccountForUpdateAsync(
        string accountId,
        string userId,
        CancellationToken cancellationToken)
    {
        if (dbContext.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
        {
            return await dbContext.Accounts
                .FromSqlInterpolated($"SELECT * FROM bank_accounts WHERE \"AccountId\" = {accountId} AND \"UserId\" = {userId} AND \"BankId\" = {bank.BankId} FOR UPDATE")
                .SingleOrDefaultAsync(cancellationToken);
        }

        return await dbContext.Accounts.SingleOrDefaultAsync(
            item => item.AccountId == accountId && item.UserId == userId && item.BankId == bank.BankId,
            cancellationToken);
    }

    private async Task<LedgerOperationResult?> FindDuplicateAsync(
        string transferId,
        string operationType,
        string accountId,
        CancellationToken cancellationToken)
    {
        var processed = await dbContext.ProcessedOperations.AsNoTracking().SingleOrDefaultAsync(
            item => item.TransferId == transferId && item.OperationType == operationType,
            cancellationToken);
        if (processed is null)
        {
            return null;
        }

        var balance = await dbContext.Accounts.AsNoTracking()
            .Where(item => item.AccountId == accountId)
            .Select(item => (decimal?)item.Balance)
            .SingleOrDefaultAsync(cancellationToken) ?? 0m;
        return processed.Outcome == "succeeded"
            ? LedgerOperationResult.Duplicate(balance)
            : new LedgerOperationResult(false, true, processed.Reason, balance, null);
    }

    private void RecordProcessed(
        string transferId,
        string operationType,
        string outcome,
        string? reason)
    {
        dbContext.ProcessedOperations.Add(new ProcessedBankOperationEntity
        {
            TransferId = transferId,
            OperationType = operationType,
            Outcome = outcome,
            Reason = reason,
            ProcessedAt = DateTimeOffset.UtcNow
        });
    }

    private void EnsureBank(string bankId)
    {
        if (!string.Equals(bank.BankId, bankId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Command for bank '{bankId}' reached '{bank.BankId}'.");
        }
    }

    public static string BuildAccountId(string userId, string bankId) => $"{userId}_{bankId}";

    private static BankLedgerEntryEntity CreateEntry(
        BankAccountEntity account,
        decimal amount,
        string direction,
        string operationType,
        string description,
        string? transferId,
        string? counterpartyUserId,
        DateTimeOffset occurredAt) => new()
        {
            LedgerEntryId = Guid.NewGuid().ToString("N"),
            AccountId = account.AccountId,
            UserId = account.UserId,
            BankId = account.BankId,
            Amount = amount,
            BalanceAfter = account.Balance,
            Direction = direction,
            OperationType = operationType,
            Description = description,
            TransferId = transferId,
            CounterpartyUserId = counterpartyUserId,
            OccurredAt = occurredAt
        };

    private static AccountDto ToAccount(BankAccountEntity account) =>
        new(account.AccountId, account.UserId, account.BankId, account.BankName, account.Balance);

    private static LedgerEntryDto ToLedgerEntry(BankLedgerEntryEntity entry) =>
        new(
            entry.LedgerEntryId,
            entry.AccountId,
            entry.UserId,
            entry.BankId,
            entry.Amount,
            entry.BalanceAfter,
            entry.Direction,
            entry.Description,
            entry.OccurredAt,
            entry.OperationType,
            entry.TransferId,
            entry.CounterpartyUserId);

    private static LedgerOperation ToOperation(BankLedgerEntryEntity entry) =>
        new(
            entry.LedgerEntryId,
            entry.AccountId,
            new Money(entry.Amount),
            entry.BalanceAfter,
            entry.Direction,
            entry.OperationType,
            entry.Description,
            entry.TransferId,
            entry.CounterpartyUserId,
            entry.OccurredAt);
}
