using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using RealtimePix.Contracts;
using RealtimePix.Eventing;

public sealed class WalletLedgerDbContext(DbContextOptions<WalletLedgerDbContext> options) : DbContext(options)
{
    public DbSet<WalletAccountEntity> Accounts => Set<WalletAccountEntity>();

    public DbSet<WalletLedgerEntryEntity> LedgerEntries => Set<WalletLedgerEntryEntity>();

    public DbSet<WalletWelcomeGrantEntity> WelcomeGrants => Set<WalletWelcomeGrantEntity>();

    public DbSet<WalletProcessedTransferEntity> ProcessedTransfers => Set<WalletProcessedTransferEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WalletAccountEntity>(entity =>
        {
            entity.ToTable("accounts");
            entity.HasKey(item => item.AccountId);
            entity.Property(item => item.AccountId).HasMaxLength(220);
            entity.Property(item => item.UserId).HasMaxLength(160).IsRequired();
            entity.Property(item => item.BankName).HasMaxLength(80).IsRequired();
            entity.Property(item => item.Balance).HasPrecision(18, 2);
            entity.HasIndex(item => new { item.UserId, item.BankName }).IsUnique();
        });

        modelBuilder.Entity<WalletLedgerEntryEntity>(entity =>
        {
            entity.ToTable("ledger_entries");
            entity.HasKey(item => item.LedgerEntryId);
            entity.Property(item => item.LedgerEntryId).HasMaxLength(64);
            entity.Property(item => item.AccountId).HasMaxLength(220).IsRequired();
            entity.Property(item => item.UserId).HasMaxLength(160).IsRequired();
            entity.Property(item => item.Amount).HasPrecision(18, 2);
            entity.Property(item => item.BalanceAfter).HasPrecision(18, 2);
            entity.Property(item => item.Direction).HasMaxLength(20).IsRequired();
            entity.Property(item => item.Description).HasMaxLength(500).IsRequired();
            entity.Property(item => item.EntryType).HasMaxLength(60).IsRequired();
            entity.Property(item => item.TransferId).HasMaxLength(64);
            entity.Property(item => item.CounterpartyUserId).HasMaxLength(160);
            entity.HasIndex(item => new { item.AccountId, item.OccurredAt });
            entity.HasIndex(item => new { item.TransferId, item.EntryType }).IsUnique().HasFilter("\"TransferId\" IS NOT NULL");
        });

        modelBuilder.Entity<WalletWelcomeGrantEntity>(entity =>
        {
            entity.ToTable("welcome_grants");
            entity.HasKey(item => item.UserId);
            entity.Property(item => item.UserId).HasMaxLength(160);
        });

        modelBuilder.Entity<WalletProcessedTransferEntity>(entity =>
        {
            entity.ToTable("processed_transfers");
            entity.HasKey(item => item.TransferId);
            entity.Property(item => item.TransferId).HasMaxLength(64);
        });

        EfCoreEventingModel.Configure(modelBuilder);
    }
}

public sealed class WalletAccountEntity
{
    public string AccountId { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;

    public string BankName { get; set; } = string.Empty;

    public decimal Balance { get; set; }
}

public sealed class WalletLedgerEntryEntity
{
    public string LedgerEntryId { get; set; } = string.Empty;

    public string AccountId { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public decimal BalanceAfter { get; set; }

    public string Direction { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; set; }

    public string EntryType { get; set; } = string.Empty;

    public string? TransferId { get; set; }

    public string? CounterpartyUserId { get; set; }
}

public sealed class WalletWelcomeGrantEntity
{
    public string UserId { get; set; } = string.Empty;

    public DateTimeOffset GrantedAt { get; set; }
}

public sealed class WalletProcessedTransferEntity
{
    public string TransferId { get; set; } = string.Empty;

    public DateTimeOffset ProcessedAt { get; set; }
}

public sealed class EfWalletLedgerStore(WalletLedgerDbContext dbContext) : IWalletLedgerStore
{
    public async Task<WalletBootstrapResult> BootstrapAsync(string userId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var ownsTransaction = dbContext.Database.CurrentTransaction is null;
        await using var transaction = ownsTransaction ? await dbContext.Database.BeginTransactionAsync(cancellationToken) : null;
        var ensured = await EnsureAccountsCoreAsync(userId, cancellationToken);
        var primaryAccountId = WalletLedgerStore.BuildAccountId(userId, WalletLedgerStore.PrimaryBankName);
        var primaryAccount = await dbContext.Accounts.SingleAsync(item => item.AccountId == primaryAccountId, cancellationToken);
        var welcomeCreditApplied = !await dbContext.WelcomeGrants.AnyAsync(item => item.UserId == userId, cancellationToken);
        LedgerEntryResponse? welcomeEntry = null;

        if (welcomeCreditApplied)
        {
            primaryAccount.Balance += WalletLedgerStore.WelcomeBalance;
            dbContext.WelcomeGrants.Add(new WalletWelcomeGrantEntity { UserId = userId, GrantedAt = DateTimeOffset.UtcNow });
            welcomeEntry = AddEntry(primaryAccount, WalletLedgerStore.WelcomeBalance, "credit", "Welcome balance", "welcome", null, null);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        if (ownsTransaction)
        {
            await transaction!.CommitAsync(cancellationToken);
        }

        return new WalletBootstrapResult(
            ToResponse(primaryAccount),
            welcomeCreditApplied,
            welcomeEntry,
            ensured.CreatedAccounts);
    }

    public async Task<AccountEnsureResult> EnsureAccountsAsync(string userId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        return await EnsureAccountsCoreAsync(userId, cancellationToken);
    }

    public async Task<DepositResult?> DepositAsync(
        string accountId,
        string userId,
        decimal amount,
        string reason,
        CancellationToken cancellationToken)
    {
        await EnsureAccountsCoreAsync(userId, cancellationToken);
        var account = await dbContext.Accounts.SingleOrDefaultAsync(item => item.AccountId == accountId && item.UserId == userId, cancellationToken);
        if (account is null)
        {
            return null;
        }

        account.Balance += amount;
        var entry = AddEntry(account, amount, "credit", reason, "deposit", null, null);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new DepositResult(entry, account.Balance);
    }

    public async Task<IReadOnlyCollection<LedgerEntryResponse>?> GetEntriesAsync(
        string accountId,
        string userId,
        CancellationToken cancellationToken)
    {
        var accountExists = await dbContext.Accounts.AnyAsync(item => item.AccountId == accountId && item.UserId == userId, cancellationToken);
        if (!accountExists)
        {
            return null;
        }

        return await dbContext.LedgerEntries
            .AsNoTracking()
            .Where(item => item.AccountId == accountId && item.UserId == userId)
            .OrderByDescending(item => item.OccurredAt)
            .Select(item => ToResponse(item))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<TransferLedgerResult> ApplyTransferAsync(PixTransferRequestedPayload transfer, CancellationToken cancellationToken)
    {
        if (await dbContext.ProcessedTransfers.AnyAsync(item => item.TransferId == transfer.TransferId, cancellationToken))
        {
            return new TransferLedgerResult(
                true,
                new DebitResult(true, null, transfer.SenderAccountId, transfer.SenderUserId, transfer.Amount, 0m),
                null);
        }

        var ownsTransaction = dbContext.Database.CurrentTransaction is null;
        await using var transaction = ownsTransaction ? await dbContext.Database.BeginTransactionAsync(cancellationToken) : null;
        dbContext.ProcessedTransfers.Add(new WalletProcessedTransferEntity
        {
            TransferId = transfer.TransferId,
            ProcessedAt = DateTimeOffset.UtcNow
        });

        await EnsureAccountsCoreAsync(transfer.SenderUserId, cancellationToken);
        var senderAccount = await dbContext.Accounts.SingleOrDefaultAsync(
            item => item.AccountId == transfer.SenderAccountId && item.UserId == transfer.SenderUserId,
            cancellationToken);

        if (senderAccount is null)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            if (ownsTransaction)
            {
                await transaction!.CommitAsync(cancellationToken);
            }

            return new TransferLedgerResult(
                false,
                new DebitResult(false, "Sender account was not found.", transfer.SenderAccountId, transfer.SenderUserId, transfer.Amount, 0m),
                null);
        }

        if (senderAccount.Balance < transfer.Amount)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            if (ownsTransaction)
            {
                await transaction!.CommitAsync(cancellationToken);
            }

            return new TransferLedgerResult(
                false,
                new DebitResult(false, "Insufficient fictional funds.", senderAccount.AccountId, senderAccount.UserId, transfer.Amount, senderAccount.Balance),
                null);
        }

        await EnsureAccountsCoreAsync(transfer.RecipientUserId, cancellationToken);
        var recipientAccount = await dbContext.Accounts.SingleOrDefaultAsync(
            item => item.AccountId == transfer.RecipientAccountId && item.UserId == transfer.RecipientUserId,
            cancellationToken);

        if (recipientAccount is null)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            if (ownsTransaction)
            {
                await transaction!.CommitAsync(cancellationToken);
            }

            return new TransferLedgerResult(
                false,
                new DebitResult(false, "Recipient account was not found.", senderAccount.AccountId, senderAccount.UserId, transfer.Amount, senderAccount.Balance),
                null);
        }

        senderAccount.Balance -= transfer.Amount;
        AddEntry(senderAccount, -transfer.Amount, "debit", $"PIX transfer {transfer.TransferId}", "pix-sent", transfer.TransferId, transfer.RecipientUserId);

        recipientAccount.Balance += transfer.Amount;
        AddEntry(recipientAccount, transfer.Amount, "credit", $"PIX transfer {transfer.TransferId}", "pix-received", transfer.TransferId, transfer.SenderUserId);

        await dbContext.SaveChangesAsync(cancellationToken);
        if (ownsTransaction)
        {
            await transaction!.CommitAsync(cancellationToken);
        }

        return new TransferLedgerResult(
            false,
            new DebitResult(true, null, senderAccount.AccountId, senderAccount.UserId, transfer.Amount, senderAccount.Balance),
            recipientAccount.Balance);
    }

    private async Task<AccountEnsureResult> EnsureAccountsCoreAsync(string userId, CancellationToken cancellationToken)
    {
        var created = new List<AccountResponse>();
        foreach (var bankName in new[] { "Bank A", "Bank B" })
        {
            var accountId = WalletLedgerStore.BuildAccountId(userId, bankName);
            var exists = await dbContext.Accounts.AnyAsync(item => item.AccountId == accountId, cancellationToken);
            if (!exists)
            {
                var account = new WalletAccountEntity
                {
                    AccountId = accountId,
                    UserId = userId,
                    BankName = bankName,
                    Balance = 0m
                };
                dbContext.Accounts.Add(account);
                created.Add(ToResponse(account));
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        var accounts = await dbContext.Accounts
            .AsNoTracking()
            .Where(item => item.UserId == userId)
            .OrderBy(item => item.BankName)
            .Select(item => ToResponse(item))
            .ToArrayAsync(cancellationToken);

        return new AccountEnsureResult(accounts, created);
    }

    private LedgerEntryResponse AddEntry(
        WalletAccountEntity account,
        decimal amount,
        string direction,
        string description,
        string entryType,
        string? transferId,
        string? counterpartyUserId)
    {
        var entry = new WalletLedgerEntryEntity
        {
            LedgerEntryId = Guid.NewGuid().ToString("N"),
            AccountId = account.AccountId,
            UserId = account.UserId,
            Amount = amount,
            BalanceAfter = account.Balance,
            Direction = direction,
            Description = description,
            OccurredAt = DateTimeOffset.UtcNow,
            EntryType = entryType,
            TransferId = transferId,
            CounterpartyUserId = counterpartyUserId
        };

        dbContext.LedgerEntries.Add(entry);
        return ToResponse(entry);
    }

    private static AccountResponse ToResponse(WalletAccountEntity account)
    {
        return new AccountResponse(account.AccountId, account.UserId, account.BankName, account.Balance);
    }

    private static LedgerEntryResponse ToResponse(WalletLedgerEntryEntity entry)
    {
        return new LedgerEntryResponse(
            entry.LedgerEntryId,
            entry.AccountId,
            entry.UserId,
            entry.Amount,
            entry.BalanceAfter,
            entry.Direction,
            entry.Description,
            entry.OccurredAt,
            entry.EntryType,
            entry.TransferId,
            entry.CounterpartyUserId);
    }
}

public sealed class EfTransactionalOperation(WalletLedgerDbContext dbContext) : ITransactionalOperation
{
    public async Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        if (dbContext.Database.CurrentTransaction is not null)
        {
            await operation(cancellationToken);
            return;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await operation(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}

public sealed class WalletLedgerDbContextFactory : IDesignTimeDbContextFactory<WalletLedgerDbContext>
{
    public WalletLedgerDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<WalletLedgerDbContext>()
            .UseNpgsql(Environment.GetEnvironmentVariable("WALLET_LEDGER_MIGRATIONS_CONNECTION")
                ?? "Host=localhost;Database=wallet_ledger_db;Username=postgres;Password=${POSTGRES_PASSWORD}")
            .Options;

        return new WalletLedgerDbContext(options);
    }
}
