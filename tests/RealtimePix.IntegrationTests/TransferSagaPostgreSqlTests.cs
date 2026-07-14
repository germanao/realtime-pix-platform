using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RealtimePix.Contracts;
using RealtimePix.Eventing;
using RealtimePix.Transaction.Application;
using RealtimePix.Transaction.Domain;
using RealtimePix.Transaction.Infrastructure;
using Xunit;

namespace RealtimePix.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class TransferSagaPostgreSqlTests(PostgreSqlFixture postgres)
{
    private static readonly DateTimeOffset Now = new(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);

    [IntegrationFact]
    public async Task Twenty_concurrent_requests_persist_one_saga_and_one_outbox_batch()
    {
        var connectionString = await CreateMigratedDatabaseAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => CreateTransferAsync(connectionString, "same-key")));

        await using var verification = CreateContext(connectionString);
        Assert.Single(results.Where(item => item.IsNew));
        Assert.Single(results.Select(item => item.Transfer.TransferId).Distinct());
        Assert.Single(await verification.TransferSagas.AsNoTracking().ToArrayAsync());
        Assert.Single(await verification.SagaTransitions.AsNoTracking().ToArrayAsync());
        Assert.Equal(3, await verification.Set<IntegrationOutboxMessage>().CountAsync());
    }

    [IntegrationFact]
    public async Task Stale_saga_transition_is_rejected_by_optimistic_concurrency()
    {
        var connectionString = await CreateMigratedDatabaseAsync();
        var started = StartSaga("concurrency-key");
        await using (var seedContext = CreateContext(connectionString))
        {
            await new EfTransferSagaRepository(seedContext)
                .AddAsync(started.Saga, started.Transition, CancellationToken.None);
        }

        await using var firstContext = CreateContext(connectionString);
        await using var staleContext = CreateContext(connectionString);
        var firstRepository = new EfTransferSagaRepository(firstContext);
        var staleRepository = new EfTransferSagaRepository(staleContext);
        var first = await firstRepository.GetAsync(started.Saga.TransferId, CancellationToken.None)
            ?? throw new InvalidOperationException("Expected the first Saga copy.");
        var stale = await staleRepository.GetAsync(started.Saga.TransferId, CancellationToken.None)
            ?? throw new InvalidOperationException("Expected the stale Saga copy.");
        var firstTransition = first.RecordDebitSucceeded(Message("winner"), Now.AddSeconds(1), TimeSpan.FromSeconds(30));
        var staleTransition = stale.RecordDebitSucceeded(Message("stale"), Now.AddSeconds(1), TimeSpan.FromSeconds(30));

        await firstRepository.SaveTransitionAsync(first, 1, firstTransition, CancellationToken.None);

        await Assert.ThrowsAsync<SagaConcurrencyException>(() =>
            staleRepository.SaveTransitionAsync(stale, 1, staleTransition, CancellationToken.None));
        await using var verification = CreateContext(connectionString);
        Assert.Equal(2, await verification.SagaTransitions.CountAsync());
        Assert.Equal(2, (await verification.TransferSagas.AsNoTracking().SingleAsync()).Version);
    }

    private async Task<string> CreateMigratedDatabaseAsync()
    {
        var connectionString = await postgres.CreateDatabaseAsync("transaction_saga");
        await using var context = CreateContext(connectionString);
        await context.Database.MigrateAsync();
        return connectionString;
    }

    private static async Task<CreateTransferResult> CreateTransferAsync(string connectionString, string idempotencyKey)
    {
        await using var context = CreateContext(connectionString);
        var repository = new EfTransferSagaRepository(context);
        var outbox = new EfCoreOutboxIntegrationEventPublisher<TransactionSagaDbContext>(
            context,
            new ConfigurationBuilder().AddInMemoryCollection().Build(),
            NullLogger<EfCoreOutboxIntegrationEventPublisher<TransactionSagaDbContext>>.Instance);
        var handler = new CreateTransferHandler(
            repository,
            new SagaMessagePublisher(outbox, outbox),
            new EfTransactionBoundary(context),
            new FixedClock(),
            new AllowSimulationPolicy(),
            Options());

        return await handler.HandleAsync(
            new PixTransferRequest(
                idempotencyKey,
                "sender",
                "sender_bank-a",
                "recipient",
                "recipient_bank-b",
                25m,
                BankIds.BankA,
                BankIds.BankB,
                SagaSimulationModes.Normal),
            CancellationToken.None);
    }

    private static StartedTransferSaga StartSaga(string idempotencyKey) =>
        TransferSaga.Start(
            "transfer-concurrency",
            idempotencyKey,
            "sender",
            "sender_bank-a",
            BankIds.BankA,
            "recipient",
            "recipient_bank-b",
            BankIds.BankB,
            new TransferAmount(25m),
            TransferSimulationMode.Normal,
            Now,
            TimeSpan.FromSeconds(30),
            Message("request"));

    private static TransactionSagaDbContext CreateContext(string connectionString) =>
        new(new DbContextOptionsBuilder<TransactionSagaDbContext>().UseNpgsql(connectionString).Options);

    private static SagaOptions Options() => new()
    {
        StepTimeout = TimeSpan.FromSeconds(30),
        SimulatedCreditTimeout = TimeSpan.FromSeconds(8),
        CompensationTimeout = TimeSpan.FromSeconds(30),
        TimeoutBatchSize = 50
    };

    private static SagaMessageMetadata Message(string id) => new(id, id, "transfer-concurrency", null);

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class AllowSimulationPolicy : ISagaSimulationPolicy
    {
        public void EnsureAllowed(TransferSimulationMode mode)
        {
        }
    }
}
