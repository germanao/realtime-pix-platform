using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace RealtimePix.Eventing;

public sealed class PostgresEventBusOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public string ConsumerName { get; set; } = string.Empty;
    public string? QueueName { get; set; }
    public int PollIntervalMilliseconds { get; set; } = 500;
}

/// <summary>
/// Durable demo transport for a single instance of each consumer. Independent
/// acknowledgements avoid cursor gaps from concurrent, out-of-order commits.
/// Delivery is at least once; service inboxes and domain handlers are idempotent.
/// </summary>
public sealed class PostgresEventBus(IOptions<PostgresEventBusOptions> options) :
    IIntegrationEventPublisher, IIntegrationMessagePublisher, IIntegrationEnvelopeTransport,
    IEventBusReadinessProbe, IAsyncDisposable
{
    private readonly NpgsqlDataSource _source = NpgsqlDataSource.Create(options.Value.ConnectionString);

    public Task PublishAsync<TPayload>(string eventType, int version, string producer, TPayload payload,
        string? correlationId = null, string? causationId = null, CancellationToken cancellationToken = default) =>
        PublishEnvelopeAsync(IntegrationMessageFactory.Create(eventType, version, producer, payload,
            IntegrationMessageKind.Event, IntegrationMessageDestination.Topic("platform-events"),
            null, correlationId, causationId), cancellationToken);

    public Task PublishCommandAsync<TPayload>(string queueName, string messageType, int version,
        string producer, TPayload payload, string? subject = null, string? correlationId = null,
        string? causationId = null, CancellationToken cancellationToken = default) =>
        PublishEnvelopeAsync(IntegrationMessageFactory.Create(messageType, version, producer, payload,
            IntegrationMessageKind.Command, IntegrationMessageDestination.Queue(queueName),
            subject, correlationId, causationId), cancellationToken);

    public async Task PublishEnvelopeAsync(EventEnvelope envelope, CancellationToken cancellationToken = default)
    {
        await using var command = _source.CreateCommand("""
            INSERT INTO bus_messages (id, kind, destination, envelope)
            VALUES ($1, $2, $3, $4::jsonb) ON CONFLICT (id) DO NOTHING
            """);
        command.Parameters.AddWithValue(envelope.EventId);
        command.Parameters.AddWithValue(envelope.MessageKind);
        command.Parameters.AddWithValue(envelope.Destination ?? "platform-events");
        command.Parameters.AddWithValue(JsonSerializer.Serialize(envelope, JsonDefaults.Options));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EventEnvelope>> ReadPendingAsync(CancellationToken cancellationToken)
    {
        await using var command = _source.CreateCommand("""
            SELECT m.envelope::text FROM bus_messages m
            WHERE (m.kind <> 'Command' OR m.destination = $2)
              AND NOT EXISTS (SELECT 1 FROM bus_acknowledgements a WHERE a.message_id = m.id AND a.consumer = $1)
            ORDER BY m.sequence LIMIT 64
            """);
        command.Parameters.AddWithValue(options.Value.ConsumerName);
        command.Parameters.AddWithValue(options.Value.QueueName ?? "");
        var envelopes = new List<EventEnvelope>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            envelopes.Add(JsonSerializer.Deserialize<EventEnvelope>(reader.GetString(0), JsonDefaults.Options)!);
        return envelopes;
    }

    public async Task AcknowledgeAsync(Guid messageId, CancellationToken cancellationToken)
    {
        await using var command = _source.CreateCommand("""
            INSERT INTO bus_acknowledgements (consumer, message_id) VALUES ($1, $2)
            ON CONFLICT DO NOTHING
            """);
        command.Parameters.AddWithValue(options.Value.ConsumerName);
        command.Parameters.AddWithValue(messageId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<EventBusReadinessResult> CheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var command = _source.CreateCommand("SELECT 1 FROM bus_messages LIMIT 1");
            await command.ExecuteScalarAsync(cancellationToken);
            return new(true, "Postgres");
        }
        catch (Exception exception) when (exception is NpgsqlException or TimeoutException)
        {
            return new(false, "Postgres", "postgres-transport-unavailable");
        }
    }

    public ValueTask DisposeAsync() => _source.DisposeAsync();
}

public sealed class PostgresEventBusWorker(PostgresEventBus bus, IOptions<PostgresEventBusOptions> options,
    IServiceScopeFactory scopeFactory, ILogger<PostgresEventBusWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                foreach (var envelope in await bus.ReadPendingAsync(stoppingToken))
                {
                    try
                    {
                        await DeliverAsync(envelope, stoppingToken);
                        await bus.AcknowledgeAsync(envelope.EventId, stoppingToken);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        // Do not acknowledge failures or block unrelated transfers behind them.
                        logger.LogWarning(exception, "Retrying durable message {EventId} for {Consumer}",
                            envelope.EventId, options.Value.ConsumerName);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Postgres transport temporarily unavailable for {Consumer}", options.Value.ConsumerName);
            }
            await Task.Delay(Math.Max(100, options.Value.PollIntervalMilliseconds), stoppingToken);
        }
    }

    private async Task DeliverAsync(EventEnvelope envelope, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var handlers = scope.ServiceProvider.GetServices<IIntegrationEventHandler>()
            .Where(handler => handler.EventTypes.Contains(envelope.EventType)).ToArray();
        if (handlers.Length == 0) return;
        var inbox = scope.ServiceProvider.GetService<IIntegrationInbox>();
        if (inbox is not null && !await inbox.TryBeginProcessingAsync(envelope, cancellationToken)) return;
        try
        {
            foreach (var handler in handlers) await handler.HandleAsync(envelope, cancellationToken);
            if (inbox is not null) await inbox.MarkProcessedAsync(envelope, cancellationToken);
        }
        catch (Exception exception)
        {
            if (inbox is not null) await inbox.MarkFailedAsync(envelope, exception, cancellationToken);
            throw;
        }
    }
}
