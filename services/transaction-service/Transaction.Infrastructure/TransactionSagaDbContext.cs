using Microsoft.EntityFrameworkCore;
using RealtimePix.Eventing;

namespace RealtimePix.Transaction.Infrastructure;

public sealed class TransactionSagaDbContext(DbContextOptions<TransactionSagaDbContext> options) : DbContext(options)
{
    public DbSet<TransferSagaEntity> TransferSagas => Set<TransferSagaEntity>();

    public DbSet<SagaTransitionEntity> SagaTransitions => Set<SagaTransitionEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TransferSagaEntity>(entity =>
        {
            entity.ToTable("transfer_sagas", table =>
            {
                table.HasCheckConstraint("CK_transfer_sagas_positive_amount", "\"Amount\" > 0");
                table.HasCheckConstraint("CK_transfer_sagas_positive_version", "\"Version\" > 0");
            });
            entity.HasKey(item => item.TransferId);
            entity.Property(item => item.TransferId).HasMaxLength(64);
            entity.Property(item => item.IdempotencyKey).HasMaxLength(160).IsRequired();
            entity.HasIndex(item => item.IdempotencyKey).IsUnique();
            entity.Property(item => item.SenderUserId).HasMaxLength(160).IsRequired();
            entity.Property(item => item.SenderAccountId).HasMaxLength(220).IsRequired();
            entity.Property(item => item.SenderBankId).HasMaxLength(40).IsRequired();
            entity.Property(item => item.RecipientUserId).HasMaxLength(160).IsRequired();
            entity.Property(item => item.RecipientAccountId).HasMaxLength(220).IsRequired();
            entity.Property(item => item.RecipientBankId).HasMaxLength(40).IsRequired();
            entity.Property(item => item.Amount).HasPrecision(18, 2);
            entity.Property(item => item.SimulationMode).HasMaxLength(40).IsRequired();
            entity.Property(item => item.State).HasMaxLength(40).IsRequired();
            entity.Property(item => item.Version).IsConcurrencyToken();
            entity.Property(item => item.FailureCode).HasMaxLength(80);
            entity.Property(item => item.FailureReason).HasMaxLength(500);
            entity.HasIndex(item => new { item.State, item.DeadlineAt });
        });

        modelBuilder.Entity<SagaTransitionEntity>(entity =>
        {
            entity.ToTable("saga_transitions");
            entity.HasKey(item => item.TransitionId);
            entity.Property(item => item.TransitionId).HasMaxLength(64);
            entity.Property(item => item.TransferId).HasMaxLength(64).IsRequired();
            entity.Property(item => item.PreviousState).HasMaxLength(40);
            entity.Property(item => item.NextState).HasMaxLength(40).IsRequired();
            entity.Property(item => item.TriggeringMessageId).HasMaxLength(100).IsRequired();
            entity.Property(item => item.TriggeringMessageType).HasMaxLength(160).IsRequired();
            entity.Property(item => item.CorrelationId).HasMaxLength(100).IsRequired();
            entity.Property(item => item.CausationId).HasMaxLength(100);
            entity.Property(item => item.Reason).HasMaxLength(500);
            entity.HasIndex(item => new { item.TransferId, item.NextVersion }).IsUnique();
            entity.HasIndex(item => new { item.TransferId, item.RecordedAt });
        });

        EfCoreEventingModel.Configure(modelBuilder);
    }
}

public sealed class TransferSagaEntity
{
    public string TransferId { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string SenderUserId { get; set; } = string.Empty;
    public string SenderAccountId { get; set; } = string.Empty;
    public string SenderBankId { get; set; } = string.Empty;
    public string RecipientUserId { get; set; } = string.Empty;
    public string RecipientAccountId { get; set; } = string.Empty;
    public string RecipientBankId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string SimulationMode { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public int Version { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset DeadlineAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? CompensationStartedAt { get; set; }
    public DateTimeOffset? CompensatedAt { get; set; }
}

public sealed class SagaTransitionEntity
{
    public string TransitionId { get; set; } = string.Empty;
    public string TransferId { get; set; } = string.Empty;
    public string? PreviousState { get; set; }
    public string NextState { get; set; } = string.Empty;
    public int PreviousVersion { get; set; }
    public int NextVersion { get; set; }
    public string TriggeringMessageId { get; set; } = string.Empty;
    public string TriggeringMessageType { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string? CausationId { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
}
