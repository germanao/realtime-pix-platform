using System.Text.Json;
using IdentityPresence.Application;
using IdentityPresence.Infrastructure;
using Microsoft.EntityFrameworkCore;
using RealtimeEvents.Application;
using RealtimeEvents.Infrastructure;
using RealtimePix.Contracts;
using RealtimePix.Eventing;
using Xunit;

namespace RealtimePix.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class ProjectionAtomicityPostgreSqlTests(PostgreSqlFixture postgres)
{
    [IntegrationFact]
    public async Task Identity_state_rolls_back_when_its_outbox_write_fails()
    {
        await using var context = await CreateContextAsync<IdentityPresenceDbContext>(
            "identity_atomicity",
            options => new IdentityPresenceDbContext(options));
        var handler = new JoinAnonymousHandler(
            new EfPresenceStore(context),
            new FailingPresencePublisher(),
            new EfPresenceTransaction(context));

        await Assert.ThrowsAsync<TestPublishException>(() =>
            handler.HandleAsync("client-atomicity", CancellationToken.None));

        Assert.Empty(await context.Users.AsNoTracking().ToArrayAsync());
        Assert.Empty(await context.Sessions.AsNoTracking().ToArrayAsync());
    }

    [IntegrationFact]
    public async Task Realtime_projection_rolls_back_when_its_outbox_write_fails()
    {
        await using var context = await CreateContextAsync<RealtimeProjectionDbContext>(
            "realtime_atomicity",
            options => new RealtimeProjectionDbContext(options));
        var notifier = new RecordingProjectionNotifier();
        var handler = new ProjectPlatformEventHandler(
            new EfRealtimeProjectionStore(context),
            notifier,
            new FailingArchitectureFlowPublisher(),
            new EfRealtimeProjectionTransaction(context));
        var platformEvent = new PlatformEvent(
            "event-atomicity",
            EventTypes.FundsDebited,
            "bank-a-ledger-service",
            "transfer-atomicity",
            "command-atomicity",
            DateTimeOffset.UtcNow,
            JsonSerializer.SerializeToElement(
                new FundsDebitedPayload(
                    "transfer-atomicity",
                    BankIds.BankA,
                    "sender_bank-a",
                    "sender",
                    10m,
                    90m),
                JsonDefaults.Options));

        await Assert.ThrowsAsync<TestPublishException>(() =>
            handler.HandleAsync(platformEvent, CancellationToken.None));

        Assert.Empty(await context.TimelineEvents.AsNoTracking().ToArrayAsync());
        Assert.Empty(await context.FlowSteps.AsNoTracking().ToArrayAsync());
        Assert.Equal(0, notifier.NotificationCount);
    }

    private async Task<TContext> CreateContextAsync<TContext>(
        string databasePrefix,
        Func<DbContextOptions<TContext>, TContext> factory)
        where TContext : DbContext
    {
        var connectionString = await postgres.CreateDatabaseAsync(databasePrefix);
        var options = new DbContextOptionsBuilder<TContext>()
            .UseNpgsql(connectionString)
            .Options;
        var context = factory(options);
        await context.Database.MigrateAsync();
        return context;
    }

    private sealed class FailingPresencePublisher : IPresenceEventPublisher
    {
        public Task UserJoinedAsync(AnonymousSessionResponse session, CancellationToken cancellationToken) =>
            throw new TestPublishException();

        public Task PresenceChangedAsync(PresenceUserResponse user, bool isOnline, CancellationToken cancellationToken) =>
            throw new TestPublishException();
    }

    private sealed class FailingArchitectureFlowPublisher : IArchitectureFlowPublisher
    {
        public Task PublishAsync(FlowStepResponse step, PlatformEvent source, CancellationToken cancellationToken) =>
            throw new TestPublishException();
    }

    private sealed class RecordingProjectionNotifier : IRealtimeProjectionNotifier
    {
        public int NotificationCount { get; private set; }

        public Task TimelineItemAsync(TimelineEventResponse item, CancellationToken cancellationToken)
        {
            NotificationCount++;
            return Task.CompletedTask;
        }

        public Task TransferFlowStepAsync(FlowStepResponse step, CancellationToken cancellationToken)
        {
            NotificationCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class TestPublishException : Exception
    {
    }
}
