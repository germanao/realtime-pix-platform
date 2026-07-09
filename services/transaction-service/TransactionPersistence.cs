using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using RealtimePix.Eventing;

public sealed class TransactionDbContext(DbContextOptions<TransactionDbContext> options) : DbContext(options)
{
    public DbSet<TransferEntity> Transfers => Set<TransferEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TransferEntity>(entity =>
        {
            entity.ToTable("transfers");
            entity.HasKey(item => item.TransferId);
            entity.Property(item => item.TransferId).HasMaxLength(64);
            entity.Property(item => item.IdempotencyKey).HasMaxLength(160).IsRequired();
            entity.HasIndex(item => item.IdempotencyKey).IsUnique();
            entity.Property(item => item.SenderUserId).HasMaxLength(160).IsRequired();
            entity.Property(item => item.SenderAccountId).HasMaxLength(220).IsRequired();
            entity.Property(item => item.RecipientUserId).HasMaxLength(160).IsRequired();
            entity.Property(item => item.RecipientAccountId).HasMaxLength(220).IsRequired();
            entity.Property(item => item.Amount).HasPrecision(18, 2);
            entity.Property(item => item.Status).HasMaxLength(40).IsRequired();
            entity.Property(item => item.FailureReason).HasMaxLength(500);
        });

        EfCoreEventingModel.Configure(modelBuilder);
    }
}

public sealed class TransferEntity
{
    public string TransferId { get; set; } = string.Empty;

    public string IdempotencyKey { get; set; } = string.Empty;

    public string SenderUserId { get; set; } = string.Empty;

    public string SenderAccountId { get; set; } = string.Empty;

    public string RecipientUserId { get; set; } = string.Empty;

    public string RecipientAccountId { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public string Status { get; set; } = string.Empty;

    public string? FailureReason { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class EfTransferStore(TransactionDbContext dbContext) : ITransferStore
{
    public async Task<CreateTransferResult> CreateAsync(PixTransferRequest request, CancellationToken cancellationToken)
    {
        var idempotencyKey = string.IsNullOrWhiteSpace(request.IdempotencyKey)
            ? Guid.NewGuid().ToString("N")
            : request.IdempotencyKey.Trim();

        var existing = await dbContext.Transfers
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.IdempotencyKey == idempotencyKey, cancellationToken);

        if (existing is not null)
        {
            return new CreateTransferResult(false, ToResponse(existing));
        }

        var now = DateTimeOffset.UtcNow;
        var transfer = new TransferEntity
        {
            TransferId = Guid.NewGuid().ToString("N"),
            IdempotencyKey = idempotencyKey,
            SenderUserId = request.SenderUserId,
            SenderAccountId = request.SenderAccountId,
            RecipientUserId = request.RecipientUserId,
            RecipientAccountId = request.RecipientAccountId,
            Amount = request.Amount,
            Status = "requested",
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.Transfers.Add(transfer);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new CreateTransferResult(true, ToResponse(transfer));
    }

    public async Task<TransferResponse?> GetAsync(string transferId, CancellationToken cancellationToken)
    {
        var transfer = await dbContext.Transfers
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.TransferId == transferId, cancellationToken);

        return transfer is null ? null : ToResponse(transfer);
    }

    public Task<TransferTransitionResult> TryMarkDebitedAsync(string transferId, CancellationToken cancellationToken)
    {
        return TryTransitionAsync(
            transferId,
            blockedStatuses: ["debited", "completed", "failed"],
            nextStatus: "debited",
            failureReason: null,
            cancellationToken);
    }

    public Task<TransferTransitionResult> TryCompleteAsync(string transferId, CancellationToken cancellationToken)
    {
        return TryTransitionAsync(
            transferId,
            blockedStatuses: ["completed", "failed"],
            nextStatus: "completed",
            failureReason: null,
            cancellationToken);
    }

    public Task<TransferTransitionResult> TryFailAsync(string transferId, string reason, CancellationToken cancellationToken)
    {
        return TryTransitionAsync(
            transferId,
            blockedStatuses: ["completed", "failed"],
            nextStatus: "failed",
            failureReason: reason,
            cancellationToken);
    }

    private async Task<TransferTransitionResult> TryTransitionAsync(
        string transferId,
        IReadOnlyCollection<string> blockedStatuses,
        string nextStatus,
        string? failureReason,
        CancellationToken cancellationToken)
    {
        var transfer = await dbContext.Transfers.SingleOrDefaultAsync(item => item.TransferId == transferId, cancellationToken);
        if (transfer is null)
        {
            return new TransferTransitionResult(false, null);
        }

        if (blockedStatuses.Contains(transfer.Status))
        {
            return new TransferTransitionResult(false, ToResponse(transfer));
        }

        transfer.Status = nextStatus;
        transfer.FailureReason = failureReason;
        transfer.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return new TransferTransitionResult(true, ToResponse(transfer));
    }

    private static TransferResponse ToResponse(TransferEntity transfer)
    {
        return new TransferResponse(
            transfer.TransferId,
            transfer.IdempotencyKey,
            transfer.SenderUserId,
            transfer.SenderAccountId,
            transfer.RecipientUserId,
            transfer.RecipientAccountId,
            transfer.Amount,
            transfer.Status,
            transfer.FailureReason,
            transfer.CreatedAt,
            transfer.UpdatedAt);
    }
}

public sealed class EfTransactionalOperation(TransactionDbContext dbContext) : ITransactionalOperation
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

public sealed class TransactionDbContextFactory : IDesignTimeDbContextFactory<TransactionDbContext>
{
    public TransactionDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TransactionDbContext>()
            .UseNpgsql(Environment.GetEnvironmentVariable("TRANSACTION_MIGRATIONS_CONNECTION")
                ?? "Host=localhost;Database=transaction_db;Username=postgres;Password=${POSTGRES_PASSWORD}")
            .Options;

        return new TransactionDbContext(options);
    }
}
