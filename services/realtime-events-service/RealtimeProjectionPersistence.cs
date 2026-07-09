using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using RealtimePix.Eventing;

public sealed class RealtimeProjectionDbContext(DbContextOptions<RealtimeProjectionDbContext> options) : DbContext(options)
{
    public DbSet<TimelineEventEntity> TimelineEvents => Set<TimelineEventEntity>();

    public DbSet<FlowStepEntity> FlowSteps => Set<FlowStepEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TimelineEventEntity>(entity =>
        {
            entity.ToTable("timeline_events");
            entity.HasKey(item => item.EventId);
            entity.Property(item => item.EventId).HasMaxLength(64);
            entity.Property(item => item.EventType).HasMaxLength(160).IsRequired();
            entity.Property(item => item.Producer).HasMaxLength(120).IsRequired();
            entity.Property(item => item.TransferId).HasMaxLength(64);
            entity.Property(item => item.CorrelationId).HasMaxLength(160).IsRequired();
            entity.Property(item => item.PayloadJson).HasColumnType("jsonb").IsRequired();
            entity.HasIndex(item => item.OccurredAt);
            entity.HasIndex(item => item.TransferId);
        });

        modelBuilder.Entity<FlowStepEntity>(entity =>
        {
            entity.ToTable("flow_steps");
            entity.HasKey(item => item.StepId);
            entity.Property(item => item.StepId).HasMaxLength(64);
            entity.Property(item => item.SourceEventId).HasMaxLength(64).IsRequired();
            entity.HasIndex(item => item.SourceEventId).IsUnique();
            entity.Property(item => item.TransferId).HasMaxLength(64);
            entity.Property(item => item.EventType).HasMaxLength(160).IsRequired();
            entity.Property(item => item.Stage).HasMaxLength(120).IsRequired();
            entity.Property(item => item.Title).HasMaxLength(160).IsRequired();
            entity.Property(item => item.Detail).HasMaxLength(700).IsRequired();
            entity.Property(item => item.Producer).HasMaxLength(120).IsRequired();
            entity.Property(item => item.CorrelationId).HasMaxLength(160).IsRequired();
            entity.Property(item => item.CausationId).HasMaxLength(160);
            entity.Property(item => item.Outcome).HasMaxLength(40).IsRequired();
            entity.HasIndex(item => new { item.TransferId, item.RecordedAt });
        });

        EfCoreEventingModel.Configure(modelBuilder);
    }
}

public sealed class TimelineEventEntity
{
    public string EventId { get; set; } = string.Empty;

    public string EventType { get; set; } = string.Empty;

    public string Producer { get; set; } = string.Empty;

    public string? TransferId { get; set; }

    public string CorrelationId { get; set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; set; }

    public string PayloadJson { get; set; } = string.Empty;
}

public sealed class FlowStepEntity
{
    public string StepId { get; set; } = string.Empty;

    public string? TransferId { get; set; }

    public string EventType { get; set; } = string.Empty;

    public string Stage { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Detail { get; set; } = string.Empty;

    public DateTimeOffset RecordedAt { get; set; }

    public string SourceEventId { get; set; } = string.Empty;

    public string Producer { get; set; } = string.Empty;

    public string CorrelationId { get; set; } = string.Empty;

    public string? CausationId { get; set; }

    public string Outcome { get; set; } = "info";
}

public sealed class EfRealtimeProjectionStore(RealtimeProjectionDbContext dbContext) : IRealtimeProjectionStore
{
    public async Task<bool> TryAddTimelineAsync(TimelineEventResponse item, CancellationToken cancellationToken)
    {
        if (await dbContext.TimelineEvents.AnyAsync(existing => existing.EventId == item.EventId, cancellationToken))
        {
            return false;
        }

        dbContext.TimelineEvents.Add(new TimelineEventEntity
        {
            EventId = item.EventId,
            EventType = item.EventType,
            Producer = item.Producer,
            TransferId = item.TransferId,
            CorrelationId = item.CorrelationId,
            OccurredAt = item.OccurredAt,
            PayloadJson = JsonSerializer.Serialize(item.Payload, JsonDefaults.Options)
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> TryAddFlowStepAsync(string sourceEventId, FlowStepResponse step, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(step.TransferId))
        {
            return false;
        }

        if (await dbContext.FlowSteps.AnyAsync(existing => existing.SourceEventId == sourceEventId, cancellationToken))
        {
            return false;
        }

        dbContext.FlowSteps.Add(new FlowStepEntity
        {
            StepId = step.StepId,
            TransferId = step.TransferId,
            EventType = step.EventType,
            Stage = step.Stage,
            Title = step.Title,
            Detail = step.Detail,
            RecordedAt = step.RecordedAt,
            SourceEventId = sourceEventId,
            Producer = step.Producer,
            CorrelationId = step.CorrelationId,
            CausationId = step.CausationId,
            Outcome = step.Outcome
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyCollection<TimelineEventResponse>> GetTimelineAsync(CancellationToken cancellationToken)
    {
        var items = await dbContext.TimelineEvents
            .AsNoTracking()
            .OrderByDescending(item => item.OccurredAt)
            .Take(250)
            .ToArrayAsync(cancellationToken);

        return items.Select(ToResponse).ToArray();
    }

    public async Task<IReadOnlyCollection<FlowStepResponse>> GetFlowAsync(string transferId, CancellationToken cancellationToken)
    {
        var items = await dbContext.FlowSteps
            .AsNoTracking()
            .Where(item => item.TransferId == transferId)
            .OrderBy(item => item.RecordedAt)
            .ToArrayAsync(cancellationToken);

        return items.Select(ToResponse).ToArray();
    }

    private static TimelineEventResponse ToResponse(TimelineEventEntity entity)
    {
        using var document = JsonDocument.Parse(entity.PayloadJson);
        return new TimelineEventResponse(
            entity.EventId,
            entity.EventType,
            entity.Producer,
            entity.TransferId,
            entity.CorrelationId,
            entity.OccurredAt,
            document.RootElement.Clone());
    }

    private static FlowStepResponse ToResponse(FlowStepEntity entity)
    {
        return new FlowStepResponse(
            entity.StepId,
            entity.TransferId,
            entity.EventType,
            entity.Stage,
            entity.Title,
            entity.Detail,
            entity.RecordedAt,
            entity.SourceEventId,
            entity.Producer,
            entity.CorrelationId,
            entity.CausationId,
            entity.Outcome);
    }
}

public sealed class RealtimeProjectionDbContextFactory : IDesignTimeDbContextFactory<RealtimeProjectionDbContext>
{
    public RealtimeProjectionDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<RealtimeProjectionDbContext>()
            .UseNpgsql(Environment.GetEnvironmentVariable("REALTIME_PROJECTION_MIGRATIONS_CONNECTION")
                ?? "Host=localhost;Database=realtime_projection_db;Username=postgres;Password=${POSTGRES_PASSWORD}")
            .Options;

        return new RealtimeProjectionDbContext(options);
    }
}
