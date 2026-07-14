using Microsoft.EntityFrameworkCore;
using RealtimePix.Eventing;

namespace RealtimePix.BankLedger.Infrastructure;

public sealed class BankLedgerDbContext(DbContextOptions<BankLedgerDbContext> options) : DbContext(options)
{
    public DbSet<BankAccountEntity> Accounts => Set<BankAccountEntity>();

    public DbSet<BankLedgerEntryEntity> LedgerEntries => Set<BankLedgerEntryEntity>();

    public DbSet<ProcessedBankOperationEntity> ProcessedOperations => Set<ProcessedBankOperationEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BankAccountEntity>(entity =>
        {
            entity.ToTable("bank_accounts");
            entity.HasKey(item => item.AccountId);
            entity.Property(item => item.AccountId).HasMaxLength(220);
            entity.Property(item => item.UserId).HasMaxLength(160).IsRequired();
            entity.Property(item => item.BankId).HasMaxLength(40).IsRequired();
            entity.Property(item => item.BankName).HasMaxLength(80).IsRequired();
            entity.Property(item => item.Balance).HasPrecision(18, 2);
            entity.HasIndex(item => new { item.UserId, item.BankId }).IsUnique();
            entity.ToTable(table => table.HasCheckConstraint("CK_bank_accounts_nonnegative_balance", "\"Balance\" >= 0"));
        });

        modelBuilder.Entity<BankLedgerEntryEntity>(entity =>
        {
            entity.ToTable("bank_ledger_entries");
            entity.HasKey(item => item.LedgerEntryId);
            entity.Property(item => item.LedgerEntryId).HasMaxLength(64);
            entity.Property(item => item.AccountId).HasMaxLength(220).IsRequired();
            entity.Property(item => item.UserId).HasMaxLength(160).IsRequired();
            entity.Property(item => item.BankId).HasMaxLength(40).IsRequired();
            entity.Property(item => item.Amount).HasPrecision(18, 2);
            entity.Property(item => item.BalanceAfter).HasPrecision(18, 2);
            entity.Property(item => item.Direction).HasMaxLength(16).IsRequired();
            entity.Property(item => item.OperationType).HasMaxLength(40).IsRequired();
            entity.Property(item => item.Description).HasMaxLength(300).IsRequired();
            entity.Property(item => item.TransferId).HasMaxLength(64);
            entity.Property(item => item.CounterpartyUserId).HasMaxLength(160);
            entity.HasIndex(item => new { item.AccountId, item.OccurredAt });
            entity.HasIndex(item => new { item.TransferId, item.OperationType })
                .IsUnique()
                .HasFilter("\"TransferId\" IS NOT NULL");
        });

        modelBuilder.Entity<ProcessedBankOperationEntity>(entity =>
        {
            entity.ToTable("processed_bank_operations");
            entity.HasKey(item => new { item.TransferId, item.OperationType });
            entity.Property(item => item.TransferId).HasMaxLength(64);
            entity.Property(item => item.OperationType).HasMaxLength(40);
            entity.Property(item => item.Outcome).HasMaxLength(24).IsRequired();
            entity.Property(item => item.Reason).HasMaxLength(300);
        });

        EfCoreEventingModel.Configure(modelBuilder);
    }
}

public sealed class BankAccountEntity
{
    public string AccountId { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;

    public string BankId { get; set; } = string.Empty;

    public string BankName { get; set; } = string.Empty;

    public decimal Balance { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class BankLedgerEntryEntity
{
    public string LedgerEntryId { get; set; } = string.Empty;

    public string AccountId { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;

    public string BankId { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public decimal BalanceAfter { get; set; }

    public string Direction { get; set; } = string.Empty;

    public string OperationType { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string? TransferId { get; set; }

    public string? CounterpartyUserId { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
}

public sealed class ProcessedBankOperationEntity
{
    public string TransferId { get; set; } = string.Empty;

    public string OperationType { get; set; } = string.Empty;

    public string Outcome { get; set; } = string.Empty;

    public string? Reason { get; set; }

    public DateTimeOffset ProcessedAt { get; set; }
}
