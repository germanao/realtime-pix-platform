using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace RealtimePix.Eventing;

public sealed class IntegrationOutboxMessage
{
    public Guid Id { get; set; }

    public string EventType { get; set; } = string.Empty;

    public string EnvelopeJson { get; set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }

    public int PublishAttempts { get; set; }

    public string? LastError { get; set; }
}

public sealed class IntegrationInboxMessage
{
    public Guid EventId { get; set; }

    public string ConsumerName { get; set; } = string.Empty;

    public string EventType { get; set; } = string.Empty;

    public DateTimeOffset ReceivedAt { get; set; }

    public DateTimeOffset? ProcessedAt { get; set; }

    public int ProcessAttempts { get; set; }

    public string? Error { get; set; }
}

public interface IIntegrationInbox
{
    Task<bool> TryBeginProcessingAsync(EventEnvelope envelope, CancellationToken cancellationToken);

    Task MarkProcessedAsync(EventEnvelope envelope, CancellationToken cancellationToken);

    Task MarkFailedAsync(EventEnvelope envelope, Exception exception, CancellationToken cancellationToken);
}

public static class EfCoreEventingModel
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IntegrationOutboxMessage>(entity =>
        {
            entity.ToTable("integration_outbox_messages");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.EventType).HasMaxLength(160).IsRequired();
            entity.Property(item => item.EnvelopeJson).HasColumnType("jsonb").IsRequired();
            entity.HasIndex(item => new { item.PublishedAt, item.OccurredAt });
        });

        modelBuilder.Entity<IntegrationInboxMessage>(entity =>
        {
            entity.ToTable("integration_inbox_messages");
            entity.HasKey(item => new { item.ConsumerName, item.EventId });
            entity.Property(item => item.ConsumerName).HasMaxLength(120).IsRequired();
            entity.Property(item => item.EventType).HasMaxLength(160).IsRequired();
            entity.HasIndex(item => new { item.ConsumerName, item.ProcessedAt });
        });
    }
}

public sealed class EfCoreOutboxIntegrationEventPublisher<TContext>(
    TContext dbContext,
    ILogger<EfCoreOutboxIntegrationEventPublisher<TContext>> logger) : IIntegrationEventPublisher
    where TContext : DbContext
{
    public async Task PublishAsync<TPayload>(
        string eventType,
        int version,
        string producer,
        TPayload payload,
        string? correlationId = null,
        string? causationId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentException.ThrowIfNullOrWhiteSpace(producer);

        var envelope = new EventEnvelope(
            Guid.NewGuid(),
            eventType,
            version,
            DateTimeOffset.UtcNow,
            correlationId ?? Guid.NewGuid().ToString("N"),
            causationId,
            producer,
            JsonSerializer.SerializeToElement(payload, JsonDefaults.Options));

        dbContext.Set<IntegrationOutboxMessage>().Add(new IntegrationOutboxMessage
        {
            Id = envelope.EventId,
            EventType = envelope.EventType,
            EnvelopeJson = JsonSerializer.Serialize(envelope, JsonDefaults.Options),
            OccurredAt = envelope.OccurredAt
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Stored integration event {EventType} {EventId} in the EF outbox.", eventType, envelope.EventId);
    }
}

public sealed class EfCoreIntegrationInbox<TContext>(
    TContext dbContext,
    IOptions<ServiceBusEventBusOptions> options) : IIntegrationInbox
    where TContext : DbContext
{
    public async Task<bool> TryBeginProcessingAsync(EventEnvelope envelope, CancellationToken cancellationToken)
    {
        var consumerName = GetConsumerName();
        var existing = await dbContext.Set<IntegrationInboxMessage>()
            .FindAsync([consumerName, envelope.EventId], cancellationToken);

        if (existing?.ProcessedAt is not null)
        {
            return false;
        }

        if (existing is null)
        {
            dbContext.Set<IntegrationInboxMessage>().Add(new IntegrationInboxMessage
            {
                ConsumerName = consumerName,
                EventId = envelope.EventId,
                EventType = envelope.EventType,
                ReceivedAt = DateTimeOffset.UtcNow,
                ProcessAttempts = 1
            });
        }
        else
        {
            existing.ProcessAttempts++;
            existing.Error = null;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task MarkProcessedAsync(EventEnvelope envelope, CancellationToken cancellationToken)
    {
        var consumerName = GetConsumerName();
        var item = await dbContext.Set<IntegrationInboxMessage>()
            .FindAsync([consumerName, envelope.EventId], cancellationToken);

        if (item is not null)
        {
            item.ProcessedAt = DateTimeOffset.UtcNow;
            item.Error = null;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task MarkFailedAsync(EventEnvelope envelope, Exception exception, CancellationToken cancellationToken)
    {
        var consumerName = GetConsumerName();
        var item = await dbContext.Set<IntegrationInboxMessage>()
            .FindAsync([consumerName, envelope.EventId], cancellationToken);

        if (item is not null)
        {
            item.Error = exception.Message;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private string GetConsumerName()
    {
        return options.Value.SubscriptionName ?? "unknown-consumer";
    }
}

public sealed class EfCoreOutboxDispatcher<TContext>(
    IServiceScopeFactory scopeFactory,
    ILogger<EfCoreOutboxDispatcher<TContext>> logger) : BackgroundService
    where TContext : DbContext
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "EF outbox dispatch batch failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }

    private async Task DispatchBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<ServiceBusIntegrationEventPublisher>();

        var pending = await dbContext.Set<IntegrationOutboxMessage>()
            .Where(item => item.PublishedAt == null && item.PublishAttempts < 10)
            .OrderBy(item => item.OccurredAt)
            .Take(20)
            .ToArrayAsync(cancellationToken);

        foreach (var item in pending)
        {
            try
            {
                var envelope = JsonSerializer.Deserialize<EventEnvelope>(item.EnvelopeJson, JsonDefaults.Options)
                    ?? throw new JsonException("Outbox envelope JSON could not be deserialized.");

                await publisher.PublishEnvelopeAsync(envelope, cancellationToken);
                item.PublishedAt = DateTimeOffset.UtcNow;
                item.LastError = null;
            }
            catch (Exception ex)
            {
                item.PublishAttempts++;
                item.LastError = ex.Message;
                logger.LogWarning(ex, "Publishing outbox event {EventId} failed.", item.Id);
            }
        }

        if (pending.Length > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}

public static class EfCoreEventingServiceCollectionExtensions
{
    public static IServiceCollection AddRealtimePixEfCoreEventing<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        services.AddScoped<IIntegrationEventPublisher, EfCoreOutboxIntegrationEventPublisher<TContext>>();
        services.AddScoped<IIntegrationInbox, EfCoreIntegrationInbox<TContext>>();
        services.AddHostedService<EfCoreOutboxDispatcher<TContext>>();
        return services;
    }
}
