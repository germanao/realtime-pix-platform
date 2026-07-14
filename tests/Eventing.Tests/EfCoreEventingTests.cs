using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RealtimePix.Contracts;
using RealtimePix.Eventing;
using Xunit;

public sealed class EfCoreEventingTests
{
    [Fact]
    public async Task Outbox_publisher_stores_cloudevent_envelope()
    {
        await using var dbContext = CreateDbContext();
        var publisher = new EfCoreOutboxIntegrationEventPublisher<TestEventingDbContext>(
            dbContext,
            new ConfigurationBuilder().AddInMemoryCollection().Build(),
            NullLogger<EfCoreOutboxIntegrationEventPublisher<TestEventingDbContext>>.Instance);

        await publisher.PublishAsync(
            EventTypes.PixTransferRequested,
            1,
            "transaction-service",
            new PixTransferRequestedPayload("transfer-1", "idem-1", "sender", "sender_bank-a", "recipient", "recipient_bank-a", 10m),
            correlationId: "transfer-1",
            cancellationToken: CancellationToken.None);

        var stored = Assert.Single(await dbContext.Set<IntegrationOutboxMessage>().ToArrayAsync());
        Assert.Equal(EventTypes.PixTransferRequested, stored.EventType);
        Assert.Null(stored.PublishedAt);

        var envelope = JsonSerializer.Deserialize<EventEnvelope>(stored.EnvelopeJson, JsonDefaults.Options)
            ?? throw new InvalidOperationException("Expected stored envelope JSON.");
        Assert.Equal(stored.Id, envelope.EventId);
        Assert.Equal("transfer-1", envelope.CorrelationId);
        Assert.Equal("transaction-service", envelope.Producer);
    }

    [Fact]
    public async Task Inbox_rejects_duplicate_after_event_is_processed()
    {
        await using var dbContext = CreateDbContext();
        var inbox = new EfCoreIntegrationInbox<TestEventingDbContext>(
            dbContext,
            Options.Create(new ServiceBusEventBusOptions { SubscriptionName = "wallet-ledger" }));
        var envelope = new EventEnvelope(
            Guid.NewGuid(),
            EventTypes.PixTransferRequested,
            1,
            DateTimeOffset.UtcNow,
            "transfer-1",
            null,
            "transaction-service",
            JsonSerializer.SerializeToElement(
                new PixTransferRequestedPayload("transfer-1", "idem-1", "sender", "sender_bank-a", "recipient", "recipient_bank-a", 10m),
                JsonDefaults.Options));

        Assert.True(await inbox.TryBeginProcessingAsync(envelope, CancellationToken.None));
        await inbox.MarkProcessedAsync(envelope, CancellationToken.None);

        Assert.False(await inbox.TryBeginProcessingAsync(envelope, CancellationToken.None));
        var stored = Assert.Single(await dbContext.Set<IntegrationInboxMessage>().ToArrayAsync());
        Assert.Equal("wallet-ledger", stored.ConsumerName);
        Assert.NotNull(stored.ProcessedAt);
    }

    private static TestEventingDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<TestEventingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new TestEventingDbContext(options);
    }

    private sealed class TestEventingDbContext(DbContextOptions<TestEventingDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            EfCoreEventingModel.Configure(modelBuilder);
        }
    }
}
