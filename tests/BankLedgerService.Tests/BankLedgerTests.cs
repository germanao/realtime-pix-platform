using RealtimePix.BankLedger.Application;
using RealtimePix.BankLedger.Domain;
using RealtimePix.BankLedger.Infrastructure;
using RealtimePix.Contracts;
using Xunit;

namespace RealtimePix.BankLedger.Tests;

public sealed class BankLedgerTests
{
    [Fact]
    public void Money_rejects_nonpositive_and_fractional_cent_values()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Money.Positive(0m));
        Assert.Throws<ArgumentOutOfRangeException>(() => Money.Positive(-1m));
        Assert.Throws<ArgumentOutOfRangeException>(() => Money.Positive(1.001m));
        Assert.Equal(1.01m, Money.Positive(1.01m).Amount);
    }

    [Fact]
    public async Task Bootstrap_creates_one_bank_account_and_applies_welcome_credit_once()
    {
        var repository = CreateRepository(welcomeBalance: 10_000m);

        var first = await repository.BootstrapAsync("user-a", CancellationToken.None);
        var second = await repository.BootstrapAsync("user-a", CancellationToken.None);

        Assert.True(first.AccountCreated);
        Assert.True(first.WelcomeCreditApplied);
        Assert.NotNull(first.WelcomeEntry);
        Assert.False(second.AccountCreated);
        Assert.False(second.WelcomeCreditApplied);
        Assert.Equal(10_000m, second.Account.Balance);
        Assert.Single(await repository.GetAccountsAsync("user-a", CancellationToken.None));
    }

    [Fact]
    public async Task Duplicate_debit_is_applied_once()
    {
        var repository = CreateRepository(welcomeBalance: 100m);
        var account = (await repository.BootstrapAsync("sender", CancellationToken.None)).Account;
        var command = DebitCommand("transfer-1", account.AccountId, 25m);

        var first = await repository.DebitAsync(command, CancellationToken.None);
        var duplicate = await repository.DebitAsync(command, CancellationToken.None);

        Assert.True(first.Succeeded);
        Assert.False(first.IsDuplicate);
        Assert.True(duplicate.Succeeded);
        Assert.True(duplicate.IsDuplicate);
        Assert.Equal(75m, Assert.Single(await repository.GetAccountsAsync("sender", CancellationToken.None)).Balance);
        Assert.Single((await repository.GetEntriesAsync(account.AccountId, "sender", CancellationToken.None))!
            .Where(item => item.EntryType == LedgerOperationTypes.Debit));
    }

    [Fact]
    public async Task Insufficient_funds_rejection_is_durable_and_idempotent()
    {
        var repository = CreateRepository(welcomeBalance: 10m);
        var account = (await repository.BootstrapAsync("sender", CancellationToken.None)).Account;
        var command = DebitCommand("transfer-1", account.AccountId, 25m);

        var first = await repository.DebitAsync(command, CancellationToken.None);
        var duplicate = await repository.DebitAsync(command, CancellationToken.None);

        Assert.False(first.Succeeded);
        Assert.Contains("Insufficient", first.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.False(duplicate.Succeeded);
        Assert.True(duplicate.IsDuplicate);
        Assert.Equal(10m, Assert.Single(await repository.GetAccountsAsync("sender", CancellationToken.None)).Balance);
    }

    [Fact]
    public async Task Credit_rejection_can_be_followed_by_exactly_one_refund()
    {
        var sender = CreateRepository(welcomeBalance: 100m);
        var recipient = new InMemoryBankLedgerRepository(new BankDescriptor(BankIds.BankB, "Bank B", 0m));
        var senderAccount = (await sender.BootstrapAsync("sender", CancellationToken.None)).Account;
        var recipientAccount = (await recipient.BootstrapAsync("recipient", CancellationToken.None)).Account;
        var debit = DebitCommand("transfer-1", senderAccount.AccountId, 25m);

        Assert.True((await sender.DebitAsync(debit, CancellationToken.None)).Succeeded);
        var credit = new CreditFundsPayload(
            "transfer-1",
            "recipient",
            recipientAccount.AccountId,
            BankIds.BankB,
            "sender",
            senderAccount.AccountId,
            BankIds.BankA,
            25m,
            SagaSimulationModes.CreditRejected);
        Assert.False((await recipient.CreditAsync(credit, CancellationToken.None)).Succeeded);

        var refund = new RefundFundsPayload(
            "transfer-1",
            "sender",
            senderAccount.AccountId,
            BankIds.BankA,
            25m,
            "Credit rejected");
        var firstRefund = await sender.RefundAsync(refund, CancellationToken.None);
        var duplicateRefund = await sender.RefundAsync(refund, CancellationToken.None);

        Assert.True(firstRefund.Succeeded);
        Assert.True(duplicateRefund.IsDuplicate);
        Assert.Equal(100m, Assert.Single(await sender.GetAccountsAsync("sender", CancellationToken.None)).Balance);
        Assert.Equal(0m, Assert.Single(await recipient.GetAccountsAsync("recipient", CancellationToken.None)).Balance);
    }

    [Fact]
    public async Task Manual_intervention_simulation_rejects_credit_and_refund_without_duplicate_mutation()
    {
        var sender = CreateRepository(welcomeBalance: 100m);
        var recipient = new InMemoryBankLedgerRepository(new BankDescriptor(BankIds.BankB, "Bank B", 0m));
        var senderAccount = (await sender.BootstrapAsync("sender", CancellationToken.None)).Account;
        var recipientAccount = (await recipient.BootstrapAsync("recipient", CancellationToken.None)).Account;
        Assert.True((await sender.DebitAsync(DebitCommand("manual-transfer", senderAccount.AccountId, 25m), CancellationToken.None)).Succeeded);

        var credit = await recipient.CreditAsync(
            new CreditFundsPayload(
                "manual-transfer",
                "recipient",
                recipientAccount.AccountId,
                BankIds.BankB,
                "sender",
                senderAccount.AccountId,
                BankIds.BankA,
                25m,
                SagaSimulationModes.RefundRejectedTest),
            CancellationToken.None);
        var refundCommand = new RefundFundsPayload(
            "manual-transfer",
            "sender",
            senderAccount.AccountId,
            BankIds.BankA,
            25m,
            "Credit rejected",
            SagaSimulationModes.RefundRejectedTest);
        var refund = await sender.RefundAsync(refundCommand, CancellationToken.None);
        var replay = await sender.RefundAsync(refundCommand, CancellationToken.None);

        Assert.False(credit.Succeeded);
        Assert.False(refund.Succeeded);
        Assert.True(replay.IsDuplicate);
        Assert.Equal(75m, Assert.Single(await sender.GetAccountsAsync("sender", CancellationToken.None)).Balance);
        Assert.Equal(0m, Assert.Single(await recipient.GetAccountsAsync("recipient", CancellationToken.None)).Balance);
    }

    [Fact]
    public async Task Concurrent_debits_never_make_balance_negative()
    {
        var repository = CreateRepository(welcomeBalance: 100m);
        var account = (await repository.BootstrapAsync("sender", CancellationToken.None)).Account;

        var results = await Task.WhenAll(Enumerable.Range(0, 100).Select(index =>
            repository.DebitAsync(
                DebitCommand($"transfer-{index}", account.AccountId, 2m),
                CancellationToken.None)));

        Assert.Equal(50, results.Count(item => item.Succeeded));
        Assert.Equal(50, results.Count(item => !item.Succeeded));
        Assert.Equal(0m, Assert.Single(await repository.GetAccountsAsync("sender", CancellationToken.None)).Balance);
    }

    [Fact]
    public async Task Broker_commands_reject_nonpositive_or_fractional_cent_amounts()
    {
        var repository = CreateRepository(welcomeBalance: 100m);
        var account = (await repository.BootstrapAsync("sender", CancellationToken.None)).Account;

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            repository.DebitAsync(DebitCommand("negative", account.AccountId, -1m), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            repository.DebitAsync(DebitCommand("fractional", account.AccountId, 1.001m), CancellationToken.None));

        Assert.Equal(100m, Assert.Single(await repository.GetAccountsAsync("sender", CancellationToken.None)).Balance);
    }

    private static InMemoryBankLedgerRepository CreateRepository(decimal welcomeBalance) =>
        new(new BankDescriptor(BankIds.BankA, "Bank A", welcomeBalance));

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
