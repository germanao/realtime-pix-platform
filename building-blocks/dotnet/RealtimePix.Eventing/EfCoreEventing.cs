using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace RealtimePix.Eventing;

public sealed class IntegrationOutboxMessage
{
    public Guid Id { get; set; }

    public string EventType { get; set; } = string.Empty;

    public string MessageKind { get; set; } = IntegrationMessageKind.Event.ToString();

    public string DestinationKind { get; set; } = IntegrationDestinationKind.Topic.ToString();

    public string Destination { get; set; } = "platform-events";

    public string EnvelopeJson { get; set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }

    public int PublishAttempts { get; set; }

    public string Status { get; set; } = "pending";

    public string? ClaimedBy { get; set; }

    public DateTimeOffset? ClaimedUntil { get; set; }

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
            entity.Property(item => item.MessageKind).HasMaxLength(24).IsRequired();
            entity.Property(item => item.DestinationKind).HasMaxLength(24).IsRequired();
            entity.Property(item => item.Destination).HasMaxLength(180).IsRequired();
            entity.Property(item => item.Status).HasMaxLength(24).IsRequired();
            entity.Property(item => item.ClaimedBy).HasMaxLength(80);
            entity.Property(item => item.EnvelopeJson).HasColumnType("jsonb").IsRequired();
            entity.HasIndex(item => new { item.PublishedAt, item.OccurredAt });
            entity.HasIndex(item => new { item.Status, item.ClaimedUntil, item.OccurredAt });
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
    IConfiguration configuration,
    ILogger<EfCoreOutboxIntegrationEventPublisher<TContext>> logger) :
    IIntegrationEventPublisher,
    IIntegrationMessagePublisher
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
        var topicName = configuration["EventBus:ServiceBus:TopicName"] ?? "platform-events";
        var envelope = IntegrationMessageFactory.Create(
            eventType,
            version,
            producer,
            payload,
            IntegrationMessageKind.Event,
            IntegrationMessageDestination.Topic(topicName),
            subject: null,
            correlationId,
            causationId);

        await StoreAsync(envelope, cancellationToken);
    }

    public async Task PublishCommandAsync<TPayload>(
        string queueName,
        string messageType,
        int version,
        string producer,
        TPayload payload,
        string? subject = null,
        string? correlationId = null,
        string? causationId = null,
        CancellationToken cancellationToken = default)
    {
        var envelope = IntegrationMessageFactory.Create(
            messageType,
            version,
            producer,
            payload,
            IntegrationMessageKind.Command,
            IntegrationMessageDestination.Queue(queueName),
            subject,
            correlationId,
            causationId);

        await StoreAsync(envelope, cancellationToken);
    }

    private async Task StoreAsync(EventEnvelope envelope, CancellationToken cancellationToken)
    {
        dbContext.Set<IntegrationOutboxMessage>().Add(new IntegrationOutboxMessage
        {
            Id = envelope.EventId,
            EventType = envelope.EventType,
            MessageKind = envelope.MessageKind,
            DestinationKind = envelope.DestinationKind,
            Destination = envelope.Destination ?? "platform-events",
            EnvelopeJson = JsonSerializer.Serialize(envelope, JsonDefaults.Options),
            OccurredAt = envelope.OccurredAt,
            Status = "pending"
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Stored {MessageKind} {EventType} {EventId} for {Destination} in the EF outbox.",
            envelope.MessageKind,
            envelope.EventType,
            envelope.EventId,
            envelope.Destination);
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
        return options.Value.QueueName ?? options.Value.SubscriptionName ?? "unknown-consumer";
    }
}

public sealed class EfCoreOutboxDispatcher<TContext>(
    IServiceScopeFactory scopeFactory,
    ILogger<EfCoreOutboxDispatcher<TContext>> logger) : BackgroundService
    where TContext : DbContext
{
    private readonly string _dispatcherId = $"{Environment.MachineName}-{Guid.NewGuid():N}";

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
        var publisher = scope.ServiceProvider.GetRequiredService<IIntegrationEnvelopeTransport>();

        var pending = await ClaimBatchAsync(dbContext, cancellationToken);

        foreach (var item in pending)
        {
            try
            {
                var envelope = JsonSerializer.Deserialize<EventEnvelope>(item.EnvelopeJson, JsonDefaults.Options)
                    ?? throw new JsonException("Outbox envelope JSON could not be deserialized.");

                await publisher.PublishEnvelopeAsync(envelope, cancellationToken);
                item.PublishedAt = DateTimeOffset.UtcNow;
                item.Status = "published";
                item.ClaimedBy = null;
                item.ClaimedUntil = null;
                item.LastError = null;
            }
            catch (Exception ex)
            {
                item.PublishAttempts++;
                item.LastError = ex.Message;
                item.ClaimedBy = null;
                item.ClaimedUntil = null;
                if (item.PublishAttempts >= 10)
                {
                    item.Status = "failed";
                }
                logger.LogWarning(ex, "Publishing outbox event {EventId} failed.", item.Id);
            }
        }

        if (pending.Length > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<IntegrationOutboxMessage[]> ClaimBatchAsync(
        TContext dbContext,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        IntegrationOutboxMessage[] pending;

        if (dbContext.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
        {
            pending = await dbContext.Set<IntegrationOutboxMessage>()
                .FromSqlRaw(
                    """
                    SELECT * FROM integration_outbox_messages
                    WHERE "PublishedAt" IS NULL
                      AND "Status" = 'pending'
                      AND ("ClaimedUntil" IS NULL OR "ClaimedUntil" < NOW())
                    ORDER BY "OccurredAt"
                    FOR UPDATE SKIP LOCKED
                    LIMIT 20
                    """)
                .ToArrayAsync(cancellationToken);
        }
        else
        {
            pending = await dbContext.Set<IntegrationOutboxMessage>()
                .Where(item => item.PublishedAt == null &&
                    item.Status == "pending" &&
                    (item.ClaimedUntil == null || item.ClaimedUntil < now))
                .OrderBy(item => item.OccurredAt)
                .Take(20)
                .ToArrayAsync(cancellationToken);
        }

        foreach (var item in pending)
        {
            item.ClaimedBy = _dispatcherId;
            item.ClaimedUntil = now.AddMinutes(2);
        }

        if (pending.Length > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return pending;
    }
}

public static class EfCoreEventingServiceCollectionExtensions
{
    public static IServiceCollection AddRealtimePixEfCoreEventing<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        services.AddScoped<EfCoreOutboxIntegrationEventPublisher<TContext>>();
        services.AddScoped<IIntegrationEventPublisher>(serviceProvider =>
            serviceProvider.GetRequiredService<EfCoreOutboxIntegrationEventPublisher<TContext>>());
        services.AddScoped<IIntegrationMessagePublisher>(serviceProvider =>
            serviceProvider.GetRequiredService<EfCoreOutboxIntegrationEventPublisher<TContext>>());
        services.AddScoped<IIntegrationInbox, EfCoreIntegrationInbox<TContext>>();
        services.AddHostedService<EfCoreOutboxDispatcher<TContext>>();
        return services;
    }
}
