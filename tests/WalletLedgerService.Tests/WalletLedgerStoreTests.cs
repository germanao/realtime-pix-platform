using System.Text.Json;
using RealtimePix.Contracts;
using RealtimePix.Eventing;
using Xunit;

public sealed class WalletLedgerStoreTests
{
    [Fact]
    public void Bootstrap_grants_welcome_balance_once_and_keeps_both_accounts()
    {
        var store = new global::WalletLedgerStore();

        var first = store.Bootstrap("user-a");
        var second = store.Bootstrap("user-a");

        Assert.True(first.WelcomeCreditApplied);
        Assert.False(second.WelcomeCreditApplied);
        Assert.NotNull(first.WelcomeEntry);
        Assert.Null(second.WelcomeEntry);
        Assert.Equal(global::WalletLedgerStore.WelcomeBalance, second.PrimaryAccount.Balance);

        var accounts = store.EnsureAccounts("user-a").Accounts;
        Assert.Equal(2, accounts.Count);
        Assert.Contains(accounts, account => account.BankName == "Bank A");
        Assert.Contains(accounts, account => account.BankName == "Bank B");

        var entries = store.GetEntries(second.PrimaryAccount.AccountId, "user-a")
            ?? throw new InvalidOperationException("Expected welcome ledger entry.");
        var welcome = Assert.Single(entries);
        Assert.Equal("welcome", welcome.EntryType);
        Assert.Equal("Welcome balance", welcome.Description);
        Assert.Equal(global::WalletLedgerStore.WelcomeBalance, welcome.BalanceAfter);
    }

    [Fact]
    public void Deposit_creates_one_ledger_entry()
    {
        var store = new global::WalletLedgerStore();
        var account = store.EnsureAccounts("user-a").Accounts.First(item => item.BankName == "Bank A");

        var result = store.Deposit(account.AccountId, "user-a", 100m, "test deposit");

        Assert.NotNull(result);
        Assert.Equal(100m, result.NewBalance);
        var entries = store.GetEntries(account.AccountId, "user-a")
            ?? throw new InvalidOperationException("Expected deposit ledger entries.");
        var entry = Assert.Single(entries);
        Assert.Equal("credit", entry.Direction);
        Assert.Equal(100m, entry.Amount);
    }

    [Fact]
    public async Task Duplicate_transfer_requested_event_does_not_debit_or_credit_twice()
    {
        var store = new global::WalletLedgerStore();
        var senderAccount = store.EnsureAccounts("sender").Accounts.First(item => item.BankName == "Bank A");
        var recipientAccount = store.EnsureAccounts("recipient").Accounts.First(item => item.BankName == "Bank A");
        store.Deposit(senderAccount.AccountId, "sender", 100m, "seed");
        var publisher = new RecordingPublisher();
        var handler = new global::PixTransferRequestedHandler(store, publisher, new global::NoopTransactionalOperation());
        var payload = new PixTransferRequestedPayload(
            "transfer-1",
            "idempotency-1",
            "sender",
            senderAccount.AccountId,
            "recipient",
            recipientAccount.AccountId,
            25m);
        var envelope = CreateEnvelope(EventTypes.PixTransferRequested, payload);

        await handler.HandleAsync(envelope, CancellationToken.None);
        await handler.HandleAsync(envelope, CancellationToken.None);

        Assert.Equal(2, publisher.Events.Count);
        Assert.Single(publisher.Events, item => item.EventType == EventTypes.PixDebitSucceeded);
        Assert.Single(publisher.Events, item => item.EventType == EventTypes.PixCreditSucceeded);
        Assert.Equal(75m, store.EnsureAccounts("sender").Accounts.First(item => item.AccountId == senderAccount.AccountId).Balance);
        Assert.Equal(25m, store.EnsureAccounts("recipient").Accounts.First(item => item.AccountId == recipientAccount.AccountId).Balance);
        var sent = Assert.Single(store.GetEntries(senderAccount.AccountId, "sender")!.Where(entry => entry.Description.Contains("transfer-1", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal("pix-sent", sent.EntryType);
        Assert.Equal("transfer-1", sent.TransferId);
        Assert.Equal("recipient", sent.CounterpartyUserId);

        var received = Assert.Single(store.GetEntries(recipientAccount.AccountId, "recipient")!);
        Assert.Equal("pix-received", received.EntryType);
        Assert.Equal("transfer-1", received.TransferId);
        Assert.Equal("sender", received.CounterpartyUserId);
    }

    [Fact]
    public async Task Insufficient_funds_emits_debit_failure_once_for_duplicate_event()
    {
        var store = new global::WalletLedgerStore();
        var senderAccount = store.EnsureAccounts("sender").Accounts.First(item => item.BankName == "Bank A");
        var recipientAccount = store.EnsureAccounts("recipient").Accounts.First(item => item.BankName == "Bank A");
        var publisher = new RecordingPublisher();
        var handler = new global::PixTransferRequestedHandler(store, publisher, new global::NoopTransactionalOperation());
        var payload = new PixTransferRequestedPayload(
            "transfer-1",
            "idempotency-1",
            "sender",
            senderAccount.AccountId,
            "recipient",
            recipientAccount.AccountId,
            25m);
        var envelope = CreateEnvelope(EventTypes.PixTransferRequested, payload);

        await handler.HandleAsync(envelope, CancellationToken.None);
        await handler.HandleAsync(envelope, CancellationToken.None);

        var failure = Assert.Single(publisher.Events);
        Assert.Equal(EventTypes.PixDebitFailed, failure.EventType);
    }

    private static EventEnvelope CreateEnvelope<TPayload>(string eventType, TPayload payload)
    {
        return new EventEnvelope(
            Guid.NewGuid(),
            eventType,
            1,
            DateTimeOffset.UtcNow,
            "correlation-1",
            null,
            "test",
            JsonSerializer.SerializeToElement(payload, JsonDefaults.Options));
    }

    private sealed class RecordingPublisher : IIntegrationEventPublisher
    {
        public List<PublishedEvent> Events { get; } = [];

        public Task PublishAsync<TPayload>(
            string eventType,
            int version,
            string producer,
            TPayload payload,
            string? correlationId = null,
            string? causationId = null,
            CancellationToken cancellationToken = default)
        {
            Events.Add(new PublishedEvent(eventType, producer, payload));
            return Task.CompletedTask;
        }
    }

    private sealed record PublishedEvent(string EventType, string Producer, object? Payload);
}
