using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RealtimePix.Eventing;
using RealtimePix.Transaction.Infrastructure;
using Xunit;

namespace RealtimePix.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class EventingPostgreSqlTests(PostgreSqlFixture postgres)
{
    [IntegrationFact]
    public async Task Multiple_dispatchers_claim_each_outbox_message_once()
    {
        var connectionString = await CreateMigratedDatabaseAsync();
        var eventIds = Enumerable.Range(0, 40).Select(_ => Guid.NewGuid()).ToArray();
        await SeedOutboxAsync(connectionString, eventIds);
        var transport = new RecordingTransport(failFirstAttempt: false);
        await using var provider = CreateServices(connectionString, transport);
        var dispatchers = new[] { CreateDispatcher(provider), CreateDispatcher(provider) };

        try
        {
            await Task.WhenAll(dispatchers.Select(item => item.StartAsync(CancellationToken.None)));
            await WaitUntilAsync(() => Task.FromResult(transport.UniqueMessageCount == eventIds.Length), TimeSpan.FromSeconds(30));
        }
        finally
        {
            await Task.WhenAll(dispatchers.Select(item => item.StopAsync(CancellationToken.None)));
        }

        Assert.Equal(eventIds.Length, transport.TotalAttempts);
        Assert.All(eventIds, eventId => Assert.Equal(1, transport.AttemptsFor(eventId)));
        await using var verification = CreateContext(connectionString);
        Assert.Equal(eventIds.Length, await verification.Set<IntegrationOutboxMessage>()
            .CountAsync(item => item.Status == "published" && item.PublishedAt != null));
    }

    [IntegrationFact]
    public async Task Ambiguous_publish_is_retried_and_remains_recoverable()
    {
        var connectionString = await CreateMigratedDatabaseAsync();
        var eventId = Guid.NewGuid();
        await SeedOutboxAsync(connectionString, [eventId]);
        var transport = new RecordingTransport(failFirstAttempt: true);
        await using var provider = CreateServices(connectionString, transport);
        var dispatcher = CreateDispatcher(provider);

        try
        {
            await dispatcher.StartAsync(CancellationToken.None);
            await WaitUntilAsync(
                async () =>
                {
                    await using var context = CreateContext(connectionString);
                    return await context.Set<IntegrationOutboxMessage>()
                        .AnyAsync(item => item.Id == eventId && item.Status == "published");
                },
                TimeSpan.FromSeconds(30));
        }
        finally
        {
            await dispatcher.StopAsync(CancellationToken.None);
        }

        Assert.Equal(2, transport.AttemptsFor(eventId));
        await using var verification = CreateContext(connectionString);
        var stored = await verification.Set<IntegrationOutboxMessage>().SingleAsync();
        Assert.Equal(1, stored.PublishAttempts);
        Assert.Equal("published", stored.Status);
        Assert.NotNull(stored.PublishedAt);
    }

    [IntegrationFact]
    public async Task Inbox_retries_an_interrupted_attempt_then_rejects_processed_replay()
    {
        var connectionString = await CreateMigratedDatabaseAsync();
        var envelope = CreateEnvelope(Guid.NewGuid());
        var options = Options.Create(new ServiceBusEventBusOptions { QueueName = "bank-a-commands" });

        await using (var interruptedContext = CreateContext(connectionString))
        {
            var inbox = new EfCoreIntegrationInbox<TransactionSagaDbContext>(interruptedContext, options);
            Assert.True(await inbox.TryBeginProcessingAsync(envelope, CancellationToken.None));
        }

        await using (var retryContext = CreateContext(connectionString))
        {
            var inbox = new EfCoreIntegrationInbox<TransactionSagaDbContext>(retryContext, options);
            Assert.True(await inbox.TryBeginProcessingAsync(envelope, CancellationToken.None));
            await inbox.MarkProcessedAsync(envelope, CancellationToken.None);
        }

        await using var replayContext = CreateContext(connectionString);
        var replayInbox = new EfCoreIntegrationInbox<TransactionSagaDbContext>(replayContext, options);
        Assert.False(await replayInbox.TryBeginProcessingAsync(envelope, CancellationToken.None));
        var stored = await replayContext.Set<IntegrationInboxMessage>().SingleAsync();
        Assert.Equal(2, stored.ProcessAttempts);
        Assert.NotNull(stored.ProcessedAt);
    }

    private async Task<string> CreateMigratedDatabaseAsync()
    {
        var connectionString = await postgres.CreateDatabaseAsync("eventing");
        await using var context = CreateContext(connectionString);
        await context.Database.MigrateAsync();
        return connectionString;
    }

    private static async Task SeedOutboxAsync(string connectionString, IEnumerable<Guid> eventIds)
    {
        await using var context = CreateContext(connectionString);
        context.Set<IntegrationOutboxMessage>().AddRange(eventIds.Select(eventId =>
        {
            var envelope = CreateEnvelope(eventId);
            return new IntegrationOutboxMessage
            {
                Id = eventId,
                EventType = envelope.EventType,
                MessageKind = envelope.MessageKind,
                DestinationKind = envelope.DestinationKind,
                Destination = envelope.Destination ?? "platform-events",
                EnvelopeJson = JsonSerializer.Serialize(envelope, JsonDefaults.Options),
                OccurredAt = envelope.OccurredAt,
                Status = "pending"
            };
        }));
        await context.SaveChangesAsync();
    }

    private static ServiceProvider CreateServices(string connectionString, RecordingTransport transport)
    {
        var services = new ServiceCollection();
        services.AddDbContext<TransactionSagaDbContext>(options => options.UseNpgsql(connectionString));
        services.AddSingleton(transport);
        services.AddSingleton<IIntegrationEnvelopeTransport>(transport);
        return services.BuildServiceProvider();
    }

    private static EfCoreOutboxDispatcher<TransactionSagaDbContext> CreateDispatcher(ServiceProvider provider) =>
        new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<EfCoreOutboxDispatcher<TransactionSagaDbContext>>.Instance);

    private static TransactionSagaDbContext CreateContext(string connectionString) =>
        new(new DbContextOptionsBuilder<TransactionSagaDbContext>().UseNpgsql(connectionString).Options);

    private static EventEnvelope CreateEnvelope(Guid eventId) => new(
        eventId,
        "IntegrationProbe.v1",
        1,
        DateTimeOffset.UtcNow,
        eventId.ToString("N"),
        null,
        "integration-tests",
        JsonSerializer.SerializeToElement(new { eventId }, JsonDefaults.Options))
    {
        MessageKind = IntegrationMessageKind.Event.ToString(),
        DestinationKind = IntegrationDestinationKind.Topic.ToString(),
        Destination = "platform-events"
    };

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"Condition was not satisfied within {timeout}.");
    }

    private sealed class RecordingTransport(bool failFirstAttempt) : IIntegrationEnvelopeTransport
    {
        private readonly ConcurrentDictionary<Guid, int> _attempts = new();

        public int TotalAttempts => _attempts.Values.Sum();

        public int UniqueMessageCount => _attempts.Count;

        public int AttemptsFor(Guid eventId) => _attempts.GetValueOrDefault(eventId);

        public Task PublishEnvelopeAsync(EventEnvelope envelope, CancellationToken cancellationToken = default)
        {
            var attempt = _attempts.AddOrUpdate(envelope.EventId, 1, (_, current) => current + 1);
            if (failFirstAttempt && attempt == 1)
            {
                throw new InvalidOperationException("Simulated ambiguous broker acknowledgement.");
            }

            return Task.CompletedTask;
        }
    }
}
