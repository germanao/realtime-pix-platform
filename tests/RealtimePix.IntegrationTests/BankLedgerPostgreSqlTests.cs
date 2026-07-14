using Microsoft.EntityFrameworkCore;
using RealtimePix.BankLedger.Application;
using RealtimePix.BankLedger.Domain;
using RealtimePix.BankLedger.Infrastructure;
using RealtimePix.Contracts;
using Xunit;

namespace RealtimePix.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class BankLedgerPostgreSqlTests(PostgreSqlFixture postgres)
{
    private static readonly BankDescriptor Bank = new(BankIds.BankA, "Bank A", 100m);

    [IntegrationFact]
    public async Task One_hundred_concurrent_debits_never_make_the_balance_negative()
    {
        var connectionString = await CreateMigratedDatabaseAsync();
        var account = await BootstrapAsync(connectionString, "sender");

        var results = await Task.WhenAll(Enumerable.Range(0, 100).Select(index =>
            DebitAsync(connectionString, DebitCommand($"transfer-{index}", account.AccountId, 2m))));

        await using var verification = CreateContext(connectionString);
        var storedAccount = await verification.Accounts.AsNoTracking().SingleAsync();
        Assert.Equal(50, results.Count(item => item.Succeeded));
        Assert.Equal(50, results.Count(item => !item.Succeeded));
        Assert.Equal(0m, storedAccount.Balance);
        Assert.Equal(50, await verification.LedgerEntries.CountAsync(item => item.OperationType == LedgerOperationTypes.Debit));
        Assert.Equal(100, await verification.ProcessedOperations.CountAsync());
    }

    [IntegrationFact]
    public async Task Concurrent_duplicate_commands_apply_one_monetary_operation()
    {
        var connectionString = await CreateMigratedDatabaseAsync();
        var account = await BootstrapAsync(connectionString, "sender");
        var command = DebitCommand("same-transfer", account.AccountId, 10m);

        var results = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => DebitAsync(connectionString, command)));

        await using var verification = CreateContext(connectionString);
        Assert.Single(results.Where(item => item.Succeeded && !item.IsDuplicate));
        Assert.Equal(19, results.Count(item => item.IsDuplicate));
        Assert.Equal(90m, (await verification.Accounts.AsNoTracking().SingleAsync()).Balance);
        Assert.Single(await verification.LedgerEntries
            .Where(item => item.TransferId == command.TransferId && item.OperationType == LedgerOperationTypes.Debit)
            .ToArrayAsync());
        Assert.Single(await verification.ProcessedOperations.ToArrayAsync());
    }

    private async Task<string> CreateMigratedDatabaseAsync()
    {
        var connectionString = await postgres.CreateDatabaseAsync("bank_ledger");
        await using var context = CreateContext(connectionString);
        await context.Database.MigrateAsync();
        return connectionString;
    }

    private static async Task<AccountDto> BootstrapAsync(string connectionString, string userId)
    {
        await using var context = CreateContext(connectionString);
        var repository = new EfBankLedgerRepository(context, Bank);
        BootstrapResult? result = null;
        await new EfTransactionalExecutor(context).ExecuteAsync(
            async cancellationToken => result = await repository.BootstrapAsync(userId, cancellationToken),
            CancellationToken.None);
        return result?.Account ?? throw new InvalidOperationException("Expected a bootstrapped account.");
    }

    private static async Task<LedgerOperationResult> DebitAsync(
        string connectionString,
        DebitFundsPayload command)
    {
        await using var context = CreateContext(connectionString);
        var repository = new EfBankLedgerRepository(context, Bank);
        LedgerOperationResult? result = null;
        await new EfTransactionalExecutor(context).ExecuteAsync(
            async cancellationToken => result = await repository.DebitAsync(command, cancellationToken),
            CancellationToken.None);
        return result ?? throw new InvalidOperationException("Expected a debit result.");
    }

    private static BankLedgerDbContext CreateContext(string connectionString) =>
        new(new DbContextOptionsBuilder<BankLedgerDbContext>().UseNpgsql(connectionString).Options);

    private static DebitFundsPayload DebitCommand(string transferId, string accountId, decimal amount) =>
        new(
            transferId,
            "sender",
            accountId,
            BankIds.BankA,
            "recipient",
            "recipient_bank-b",
            BankIds.BankB,
            amount,
            SagaSimulationModes.Normal);
}
