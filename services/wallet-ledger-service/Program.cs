using RealtimePix.Contracts;
using RealtimePix.Eventing;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.EntityFrameworkCore;

const string ServiceName = "wallet-ledger-service";

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddRealtimePixAzureAppConfiguration();
builder.Services.AddCors();
builder.Services.AddOpenTelemetry().UseAzureMonitor();

var defaultConnectionString = builder.Configuration.GetConnectionString("Default");
if (string.IsNullOrWhiteSpace(defaultConnectionString))
{
    builder.Services.AddSingleton<WalletLedgerStore>();
    builder.Services.AddSingleton<IWalletLedgerStore>(serviceProvider =>
        new InMemoryWalletLedgerStoreAdapter(serviceProvider.GetRequiredService<WalletLedgerStore>()));
    builder.Services.AddSingleton<ITransactionalOperation, NoopTransactionalOperation>();
    builder.Services.AddSingleton<IIntegrationEventHandler, PixTransferRequestedHandler>();
}
else
{
    builder.Services.AddDbContext<WalletLedgerDbContext>(options => options.UseNpgsql(defaultConnectionString));
    builder.Services.AddScoped<IWalletLedgerStore, EfWalletLedgerStore>();
    builder.Services.AddScoped<ITransactionalOperation, EfTransactionalOperation>();
    builder.Services.AddScoped<IIntegrationEventHandler, PixTransferRequestedHandler>();
}

builder.Services.AddRealtimePixEventBus(builder.Configuration, ServiceName);
if (!string.IsNullOrWhiteSpace(defaultConnectionString))
{
    builder.Services.AddRealtimePixEfCoreEventing<WalletLedgerDbContext>();
}

var app = builder.Build();
app.UseCors(policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? ["http://localhost:3000"])
    .AllowAnyHeader()
    .AllowAnyMethod());

app.MapGet("/health", () => Results.Ok(new { service = ServiceName, status = "ok" }));
app.MapGet("/health/live", () => Results.Ok(new { service = ServiceName, status = "live" }));
app.MapGet("/health/ready", () => Results.Ok(new { service = ServiceName, status = "ready" }));

app.MapPost("/wallet/users/{userId}/bootstrap", async (
    string userId,
    IWalletLedgerStore store,
    IIntegrationEventPublisher publisher,
    ITransactionalOperation transactionalOperation,
    CancellationToken cancellationToken) =>
{
    WalletBootstrapResult? result = null;
    await transactionalOperation.ExecuteAsync(async innerCancellationToken =>
    {
        result = await store.BootstrapAsync(userId, innerCancellationToken);
        foreach (var account in result.CreatedAccounts)
        {
            await publisher.PublishAsync(
                EventTypes.AccountCreated,
                1,
                ServiceName,
                new AccountCreatedPayload(account.AccountId, account.UserId, account.BankName, account.Balance),
                correlationId: account.UserId,
                cancellationToken: innerCancellationToken);
        }

        if (result.WelcomeEntry is not null)
        {
            await publisher.PublishAsync(
                EventTypes.FundsDeposited,
                1,
                ServiceName,
                new FundsDepositedPayload(
                    result.WelcomeEntry.LedgerEntryId,
                    result.WelcomeEntry.AccountId,
                    result.WelcomeEntry.UserId,
                    result.WelcomeEntry.Amount,
                    result.WelcomeEntry.BalanceAfter,
                    result.WelcomeEntry.Description),
                correlationId: userId,
                cancellationToken: innerCancellationToken);
        }
    }, cancellationToken);

    var response = result ?? throw new InvalidOperationException("Wallet bootstrap did not produce a result.");
    return Results.Ok(new WalletBootstrapResponse(response.PrimaryAccount, response.WelcomeCreditApplied));
});

app.MapGet("/wallet/accounts", async (
    string userId,
    IWalletLedgerStore store,
    IIntegrationEventPublisher publisher,
    ITransactionalOperation transactionalOperation,
    CancellationToken cancellationToken) =>
{
    AccountEnsureResult? result = null;
    await transactionalOperation.ExecuteAsync(async innerCancellationToken =>
    {
        result = await store.EnsureAccountsAsync(userId, innerCancellationToken);
        foreach (var account in result.CreatedAccounts)
        {
            await publisher.PublishAsync(
                EventTypes.AccountCreated,
                1,
                ServiceName,
                new AccountCreatedPayload(account.AccountId, account.UserId, account.BankName, account.Balance),
                correlationId: account.UserId,
                cancellationToken: innerCancellationToken);
        }
    }, cancellationToken);

    var response = result ?? throw new InvalidOperationException("Wallet account lookup did not produce a result.");
    return Results.Ok(response.Accounts);
});

app.MapPost("/wallet/accounts/{accountId}/deposit", async (
    string accountId,
    DepositRequest request,
    IWalletLedgerStore store,
    IIntegrationEventPublisher publisher,
    ITransactionalOperation transactionalOperation,
    CancellationToken cancellationToken) =>
{
    if (request.Amount <= 0)
    {
        return Results.BadRequest(new { message = "Deposit amount must be positive." });
    }

    DepositResult? result = null;
    await transactionalOperation.ExecuteAsync(async innerCancellationToken =>
    {
        result = await store.DepositAsync(accountId, request.UserId, request.Amount, request.Reason ?? "Manual demo deposit", innerCancellationToken);
        if (result is null)
        {
            return;
        }

        await publisher.PublishAsync(
            EventTypes.FundsDeposited,
            1,
            ServiceName,
            new FundsDepositedPayload(
                result.Entry.LedgerEntryId,
                result.Entry.AccountId,
                result.Entry.UserId,
                result.Entry.Amount,
                result.NewBalance,
                result.Entry.Description),
            correlationId: result.Entry.UserId,
            cancellationToken: innerCancellationToken);
    }, cancellationToken);

    if (result is null)
    {
        return Results.NotFound(new { message = "Account was not found for this user." });
    }

    return Results.Ok(result);
});

app.MapGet("/wallet/accounts/{accountId}/transactions", async (
    string accountId,
    string userId,
    IWalletLedgerStore store,
    CancellationToken cancellationToken) =>
{
    var entries = await store.GetEntriesAsync(accountId, userId, cancellationToken);
    return entries is null ? Results.NotFound(new { message = "Account was not found for this user." }) : Results.Ok(entries);
});

app.Run();

public sealed record DepositRequest(string UserId, decimal Amount, string? Reason);

public sealed record AccountResponse(string AccountId, string UserId, string BankName, decimal Balance);

public sealed record AccountEnsureResult(IReadOnlyCollection<AccountResponse> Accounts, IReadOnlyCollection<AccountResponse> CreatedAccounts);

public sealed record WalletBootstrapResponse(AccountResponse PrimaryAccount, bool WelcomeCreditApplied);

public sealed record WalletBootstrapResult(
    AccountResponse PrimaryAccount,
    bool WelcomeCreditApplied,
    LedgerEntryResponse? WelcomeEntry,
    IReadOnlyCollection<AccountResponse> CreatedAccounts);

public sealed record LedgerEntryResponse(
    string LedgerEntryId,
    string AccountId,
    string UserId,
    decimal Amount,
    decimal BalanceAfter,
    string Direction,
    string Description,
    DateTimeOffset OccurredAt,
    string EntryType,
    string? TransferId,
    string? CounterpartyUserId);

public sealed record DepositResult(LedgerEntryResponse Entry, decimal NewBalance);

public sealed record DebitResult(bool Succeeded, string? Reason, string AccountId, string UserId, decimal Amount, decimal NewBalance);

public sealed record TransferLedgerResult(bool IsDuplicate, DebitResult Debit, decimal? RecipientBalance);

public interface IWalletLedgerStore
{
    Task<WalletBootstrapResult> BootstrapAsync(string userId, CancellationToken cancellationToken);

    Task<AccountEnsureResult> EnsureAccountsAsync(string userId, CancellationToken cancellationToken);

    Task<DepositResult?> DepositAsync(string accountId, string userId, decimal amount, string reason, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<LedgerEntryResponse>?> GetEntriesAsync(string accountId, string userId, CancellationToken cancellationToken);

    Task<TransferLedgerResult> ApplyTransferAsync(PixTransferRequestedPayload transfer, CancellationToken cancellationToken);
}

public interface ITransactionalOperation
{
    Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken);
}

public sealed class NoopTransactionalOperation : ITransactionalOperation
{
    public Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        return operation(cancellationToken);
    }
}

public sealed class InMemoryWalletLedgerStoreAdapter(WalletLedgerStore inner) : IWalletLedgerStore
{
    public Task<WalletBootstrapResult> BootstrapAsync(string userId, CancellationToken cancellationToken)
    {
        return Task.FromResult(inner.Bootstrap(userId));
    }

    public Task<AccountEnsureResult> EnsureAccountsAsync(string userId, CancellationToken cancellationToken)
    {
        return Task.FromResult(inner.EnsureAccounts(userId));
    }

    public Task<DepositResult?> DepositAsync(string accountId, string userId, decimal amount, string reason, CancellationToken cancellationToken)
    {
        return Task.FromResult(inner.Deposit(accountId, userId, amount, reason));
    }

    public Task<IReadOnlyCollection<LedgerEntryResponse>?> GetEntriesAsync(string accountId, string userId, CancellationToken cancellationToken)
    {
        return Task.FromResult(inner.GetEntries(accountId, userId));
    }

    public Task<TransferLedgerResult> ApplyTransferAsync(PixTransferRequestedPayload transfer, CancellationToken cancellationToken)
    {
        return Task.FromResult(inner.ApplyTransfer(transfer));
    }
}

public sealed class WalletLedgerStore : IWalletLedgerStore
{
    public const decimal WelcomeBalance = 10_000m;
    public const string PrimaryBankName = "Bank A";

    private readonly object _gate = new();
    private readonly Dictionary<string, AccountState> _accounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<LedgerEntryResponse>> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _processedTransferIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _welcomeGrantedUsers = new(StringComparer.OrdinalIgnoreCase);

    public Task<WalletBootstrapResult> BootstrapAsync(string userId, CancellationToken cancellationToken)
    {
        return Task.FromResult(Bootstrap(userId));
    }

    public Task<AccountEnsureResult> EnsureAccountsAsync(string userId, CancellationToken cancellationToken)
    {
        return Task.FromResult(EnsureAccounts(userId));
    }

    public Task<DepositResult?> DepositAsync(string accountId, string userId, decimal amount, string reason, CancellationToken cancellationToken)
    {
        return Task.FromResult(Deposit(accountId, userId, amount, reason));
    }

    public Task<IReadOnlyCollection<LedgerEntryResponse>?> GetEntriesAsync(string accountId, string userId, CancellationToken cancellationToken)
    {
        return Task.FromResult(GetEntries(accountId, userId));
    }

    public Task<TransferLedgerResult> ApplyTransferAsync(PixTransferRequestedPayload transfer, CancellationToken cancellationToken)
    {
        return Task.FromResult(ApplyTransfer(transfer));
    }

    public WalletBootstrapResult Bootstrap(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        lock (_gate)
        {
            var ensured = EnsureAccounts(userId);
            var primaryAccountId = BuildAccountId(userId, PrimaryBankName);
            var primaryAccount = _accounts[primaryAccountId];
            LedgerEntryResponse? welcomeEntry = null;
            var welcomeCreditApplied = _welcomeGrantedUsers.Add(userId);

            if (welcomeCreditApplied)
            {
                primaryAccount.Balance += WelcomeBalance;
                welcomeEntry = AddEntry(
                    primaryAccount,
                    WelcomeBalance,
                    "credit",
                    "Welcome balance",
                    "welcome",
                    null,
                    null);
            }

            return new WalletBootstrapResult(
                ToResponse(primaryAccount),
                welcomeCreditApplied,
                welcomeEntry,
                ensured.CreatedAccounts);
        }
    }

    public AccountEnsureResult EnsureAccounts(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        lock (_gate)
        {
            var created = new List<AccountResponse>();
            foreach (var bankName in new[] { "Bank A", "Bank B" })
            {
                var accountId = BuildAccountId(userId, bankName);
                if (!_accounts.ContainsKey(accountId))
                {
                    var account = new AccountState(accountId, userId, bankName, 0m);
                    _accounts[accountId] = account;
                    _entries[accountId] = [];
                    created.Add(ToResponse(account));
                }
            }

            return new AccountEnsureResult(
                _accounts.Values.Where(account => account.UserId == userId).Select(ToResponse).ToArray(),
                created);
        }
    }

    public DepositResult? Deposit(string accountId, string userId, decimal amount, string reason)
    {
        lock (_gate)
        {
            EnsureAccounts(userId);
            if (!_accounts.TryGetValue(accountId, out var account) || account.UserId != userId)
            {
                return null;
            }

            account.Balance += amount;
            var entry = AddEntry(account, amount, "credit", reason, "deposit", null, null);
            return new DepositResult(entry, account.Balance);
        }
    }

    public IReadOnlyCollection<LedgerEntryResponse>? GetEntries(string accountId, string userId)
    {
        lock (_gate)
        {
            if (!_accounts.TryGetValue(accountId, out var account) || account.UserId != userId)
            {
                return null;
            }

            return _entries[accountId].OrderByDescending(entry => entry.OccurredAt).ToArray();
        }
    }

    public DebitResult DebitForTransfer(PixTransferRequestedPayload transfer)
    {
        lock (_gate)
        {
            EnsureAccounts(transfer.SenderUserId);
            if (!_accounts.TryGetValue(transfer.SenderAccountId, out var account) || account.UserId != transfer.SenderUserId)
            {
                return new DebitResult(false, "Sender account was not found.", transfer.SenderAccountId, transfer.SenderUserId, transfer.Amount, 0m);
            }

            if (account.Balance < transfer.Amount)
            {
                return new DebitResult(false, "Insufficient fictional funds.", account.AccountId, account.UserId, transfer.Amount, account.Balance);
            }

            account.Balance -= transfer.Amount;
            AddEntry(
                account,
                -transfer.Amount,
                "debit",
                $"PIX transfer {transfer.TransferId}",
                "pix-sent",
                transfer.TransferId,
                transfer.RecipientUserId);
            return new DebitResult(true, null, account.AccountId, account.UserId, transfer.Amount, account.Balance);
        }
    }

    public decimal CreditForTransfer(PixTransferRequestedPayload transfer)
    {
        lock (_gate)
        {
            EnsureAccounts(transfer.RecipientUserId);
            var account = _accounts[transfer.RecipientAccountId];
            account.Balance += transfer.Amount;
            AddEntry(
                account,
                transfer.Amount,
                "credit",
                $"PIX transfer {transfer.TransferId}",
                "pix-received",
                transfer.TransferId,
                transfer.SenderUserId);
            return account.Balance;
        }
    }

    public TransferLedgerResult ApplyTransfer(PixTransferRequestedPayload transfer)
    {
        lock (_gate)
        {
            if (!_processedTransferIds.Add(transfer.TransferId))
            {
                return new TransferLedgerResult(
                    true,
                    new DebitResult(true, null, transfer.SenderAccountId, transfer.SenderUserId, transfer.Amount, 0m),
                    null);
            }

            EnsureAccounts(transfer.SenderUserId);
            if (!_accounts.TryGetValue(transfer.SenderAccountId, out var senderAccount) || senderAccount.UserId != transfer.SenderUserId)
            {
                return new TransferLedgerResult(
                    false,
                    new DebitResult(false, "Sender account was not found.", transfer.SenderAccountId, transfer.SenderUserId, transfer.Amount, 0m),
                    null);
            }

            if (senderAccount.Balance < transfer.Amount)
            {
                return new TransferLedgerResult(
                    false,
                    new DebitResult(false, "Insufficient fictional funds.", senderAccount.AccountId, senderAccount.UserId, transfer.Amount, senderAccount.Balance),
                    null);
            }

            EnsureAccounts(transfer.RecipientUserId);
            if (!_accounts.TryGetValue(transfer.RecipientAccountId, out var recipientAccount) || recipientAccount.UserId != transfer.RecipientUserId)
            {
                return new TransferLedgerResult(
                    false,
                    new DebitResult(false, "Recipient account was not found.", transfer.SenderAccountId, transfer.SenderUserId, transfer.Amount, senderAccount.Balance),
                    null);
            }

            senderAccount.Balance -= transfer.Amount;
            AddEntry(
                senderAccount,
                -transfer.Amount,
                "debit",
                $"PIX transfer {transfer.TransferId}",
                "pix-sent",
                transfer.TransferId,
                transfer.RecipientUserId);

            recipientAccount.Balance += transfer.Amount;
            AddEntry(
                recipientAccount,
                transfer.Amount,
                "credit",
                $"PIX transfer {transfer.TransferId}",
                "pix-received",
                transfer.TransferId,
                transfer.SenderUserId);

            return new TransferLedgerResult(
                false,
                new DebitResult(true, null, senderAccount.AccountId, senderAccount.UserId, transfer.Amount, senderAccount.Balance),
                recipientAccount.Balance);
        }
    }

    public static string BuildAccountId(string userId, string bankName)
    {
        return $"{userId}_{bankName.Replace(" ", "-", StringComparison.OrdinalIgnoreCase).ToLowerInvariant()}";
    }

    private LedgerEntryResponse AddEntry(
        AccountState account,
        decimal amount,
        string direction,
        string description,
        string entryType,
        string? transferId,
        string? counterpartyUserId)
    {
        var entry = new LedgerEntryResponse(
            Guid.NewGuid().ToString("N"),
            account.AccountId,
            account.UserId,
            amount,
            account.Balance,
            direction,
            description,
            DateTimeOffset.UtcNow,
            entryType,
            transferId,
            counterpartyUserId);

        _entries[account.AccountId].Add(entry);
        return entry;
    }

    private static AccountResponse ToResponse(AccountState account)
    {
        return new AccountResponse(account.AccountId, account.UserId, account.BankName, account.Balance);
    }

    private sealed class AccountState(string accountId, string userId, string bankName, decimal balance)
    {
        public string AccountId { get; } = accountId;
        public string UserId { get; } = userId;
        public string BankName { get; } = bankName;
        public decimal Balance { get; set; } = balance;
    }
}

public sealed class PixTransferRequestedHandler(
    IWalletLedgerStore store,
    IIntegrationEventPublisher publisher,
    ITransactionalOperation transactionalOperation) : IIntegrationEventHandler
{
    public IReadOnlyCollection<string> EventTypes { get; } = [RealtimePix.Contracts.EventTypes.PixTransferRequested];

    public async Task HandleAsync(EventEnvelope envelope, CancellationToken cancellationToken)
    {
        await transactionalOperation.ExecuteAsync(async innerCancellationToken =>
        {
            var transfer = envelope.DeserializePayload<PixTransferRequestedPayload>();
            var result = await store.ApplyTransferAsync(transfer, innerCancellationToken);
            if (result.IsDuplicate)
            {
                return;
            }

            if (!result.Debit.Succeeded)
            {
                await publisher.PublishAsync(
                    RealtimePix.Contracts.EventTypes.PixDebitFailed,
                    1,
                    WalletLedgerServiceMetadata.Name,
                    new PixDebitFailedPayload(transfer.TransferId, transfer.SenderUserId, transfer.SenderAccountId, transfer.Amount, result.Debit.Reason ?? "Debit failed."),
                    correlationId: envelope.CorrelationId,
                    causationId: envelope.EventId.ToString("N"),
                    cancellationToken: innerCancellationToken);
                return;
            }

            await publisher.PublishAsync(
                RealtimePix.Contracts.EventTypes.PixDebitSucceeded,
                1,
                WalletLedgerServiceMetadata.Name,
                new PixDebitSucceededPayload(transfer.TransferId, transfer.SenderUserId, transfer.SenderAccountId, transfer.Amount, result.Debit.NewBalance),
                correlationId: envelope.CorrelationId,
                causationId: envelope.EventId.ToString("N"),
                cancellationToken: innerCancellationToken);

            await publisher.PublishAsync(
                RealtimePix.Contracts.EventTypes.PixCreditSucceeded,
                1,
                WalletLedgerServiceMetadata.Name,
                new PixCreditSucceededPayload(transfer.TransferId, transfer.RecipientUserId, transfer.RecipientAccountId, transfer.Amount, result.RecipientBalance ?? 0m),
                correlationId: envelope.CorrelationId,
                causationId: envelope.EventId.ToString("N"),
                cancellationToken: innerCancellationToken);
        }, cancellationToken);
    }
}

public static class WalletLedgerServiceMetadata
{
    public const string Name = "wallet-ledger-service";
}
