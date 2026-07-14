using RealtimePix.Contracts;
using RealtimePix.Transaction.Application;
using RealtimePix.Transaction.Domain;
using RealtimePix.Transaction.Infrastructure;
using Xunit;

namespace RealtimePix.Transaction.Tests;

public sealed class TransferSagaTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Normal_saga_follows_debit_credit_completed_sequence()
    {
        var started = StartSaga();

        var debit = started.Saga.RecordDebitSucceeded(Message("debit"), Now.AddSeconds(1), TimeSpan.FromSeconds(30));
        var credit = started.Saga.RecordCreditSucceeded(Message("credit"), Now.AddSeconds(2));

        Assert.Equal(TransferSagaState.DebitPending, started.Transition.NextState);
        Assert.Equal(TransferSagaState.CreditPending, debit.NextState);
        Assert.Equal(TransferSagaState.Completed, credit.NextState);
        Assert.Equal(3, started.Saga.Version);
        Assert.True(started.Saga.IsTerminal);
        Assert.Throws<InvalidSagaTransitionException>(() =>
            started.Saga.RecordCreditSucceeded(Message("duplicate-credit"), Now.AddSeconds(3)));
    }

    [Fact]
    public void Credit_rejection_requires_refund_before_saga_is_compensated()
    {
        var started = StartSaga(TransferSimulationMode.CreditRejected);
        started.Saga.RecordDebitSucceeded(Message("debit"), Now.AddSeconds(1), TimeSpan.FromSeconds(30));

        var rejected = started.Saga.RecordCreditRejected(
            Message("credit-rejected"),
            "Recipient bank rejected credit.",
            Now.AddSeconds(2),
            TimeSpan.FromSeconds(30));
        var refunded = started.Saga.RecordRefundSucceeded(Message("refund"), Now.AddSeconds(3));

        Assert.Equal(TransferSagaState.CompensationPending, rejected.NextState);
        Assert.Equal(TransferSagaState.Compensated, refunded.NextState);
        Assert.Equal("credit_rejected", started.Saga.FailureCode);
        Assert.NotNull(started.Saga.CompensatedAt);
    }

    [Fact]
    public void Credit_and_refund_timeouts_end_in_manual_intervention()
    {
        var started = StartSaga(TransferSimulationMode.CreditTimeout);
        started.Saga.RecordDebitSucceeded(Message("debit"), Now.AddSeconds(1), TimeSpan.FromSeconds(8));

        var creditTimeout = started.Saga.RecordCreditTimeout(
            Message("credit-timeout"),
            Now.AddSeconds(9),
            TimeSpan.FromSeconds(30));
        var refundTimeout = started.Saga.RecordCompensationTimeout(
            Message("refund-timeout"),
            Now.AddSeconds(40));

        Assert.Equal(TransferSagaState.CompensationPending, creditTimeout.NextState);
        Assert.Equal(TransferSagaState.ManualIntervention, refundTimeout.NextState);
        Assert.Equal("refund_timeout", started.Saga.FailureCode);
        Assert.True(started.Saga.IsTerminal);
    }

    [Fact]
    public void Refund_rejection_finishes_in_manual_intervention()
    {
        var started = StartSaga(TransferSimulationMode.RefundRejectedTest);
        started.Saga.RecordDebitSucceeded(Message("debit"), Now.AddSeconds(1), TimeSpan.FromSeconds(30));
        started.Saga.RecordCreditRejected(
            Message("credit-rejected"),
            "Recipient bank rejected credit.",
            Now.AddSeconds(2),
            TimeSpan.FromSeconds(30));

        var transition = started.Saga.RecordRefundRejected(
            Message("refund-rejected"),
            "Sender bank rejected refund.",
            Now.AddSeconds(3));

        Assert.Equal(TransferSagaState.ManualIntervention, transition.NextState);
        Assert.Equal("refund_rejected", started.Saga.FailureCode);
        Assert.True(started.Saga.IsTerminal);
    }

    [Fact]
    public void Debit_rejection_finishes_failed_without_compensation()
    {
        var started = StartSaga();

        var transition = started.Saga.RecordDebitRejected(
            Message("debit-rejected"),
            "Insufficient fictional funds.",
            Now.AddSeconds(1));

        Assert.Equal(TransferSagaState.Failed, transition.NextState);
        Assert.Equal("debit_rejected", started.Saga.FailureCode);
        Assert.Null(started.Saga.CompensationStartedAt);
    }

    [Fact]
    public async Task Twenty_concurrent_requests_with_one_idempotency_key_create_one_saga()
    {
        var repository = new InMemoryTransferSagaRepository();
        var publisher = new RecordingSagaPublisher();
        var handler = CreateHandler(repository, publisher);
        var request = Request("same-key");

        var results = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => handler.HandleAsync(request, CancellationToken.None)));

        Assert.Single(results.Where(item => item.IsNew));
        Assert.Single(results.Select(item => item.Transfer.TransferId).Distinct());
        Assert.Equal(1, publisher.Count("debit-command"));
        Assert.Equal(1, publisher.Count("legacy-requested"));
        Assert.Equal(1, publisher.Count("transition"));
    }

    [Fact]
    public async Task Duplicate_outcome_event_does_not_advance_saga_twice()
    {
        var repository = new InMemoryTransferSagaRepository();
        var publisher = new RecordingSagaPublisher();
        var clock = new FakeClock(Now);
        var created = await CreateHandler(repository, publisher, clock)
            .HandleAsync(Request("key-1"), CancellationToken.None);
        var handler = new ProcessSagaOutcomeHandler(
            repository,
            publisher,
            new NoopTransactionBoundary(),
            clock,
            Options());
        var payload = new FundsDebitedPayload(
            created.Transfer.TransferId,
            BankIds.BankA,
            created.Transfer.SenderAccountId,
            created.Transfer.SenderUserId,
            created.Transfer.Amount,
            75m);

        var first = await handler.HandleFundsDebitedAsync(payload, Message("debit-event"), CancellationToken.None);
        var duplicate = await handler.HandleFundsDebitedAsync(payload, Message("debit-event-copy"), CancellationToken.None);

        Assert.True(first.Changed);
        Assert.False(duplicate.Changed);
        Assert.Equal(TransferSagaStates.CreditPending, first.Transfer!.SagaState);
        Assert.Equal(1, publisher.Count("credit-command"));
        Assert.Equal(2, (await repository.GetTransitionsAsync(created.Transfer.TransferId, CancellationToken.None)).Count);
    }

    [Fact]
    public async Task Mismatched_bank_outcome_cannot_advance_the_saga()
    {
        var repository = new InMemoryTransferSagaRepository();
        var publisher = new RecordingSagaPublisher();
        var clock = new FakeClock(Now);
        var created = await CreateHandler(repository, publisher, clock)
            .HandleAsync(Request("mismatched-outcome"), CancellationToken.None);
        var handler = new ProcessSagaOutcomeHandler(
            repository,
            publisher,
            new NoopTransactionBoundary(),
            clock,
            Options());
        var mismatched = new FundsDebitedPayload(
            created.Transfer.TransferId,
            BankIds.BankB,
            created.Transfer.SenderAccountId,
            created.Transfer.SenderUserId,
            created.Transfer.Amount,
            75m);

        await Assert.ThrowsAsync<InvalidSagaOutcomeException>(() =>
            handler.HandleFundsDebitedAsync(mismatched, Message("wrong-bank"), CancellationToken.None));

        var unchanged = await repository.GetAsync(created.Transfer.TransferId, CancellationToken.None);
        Assert.Equal(TransferSagaState.DebitPending, unchanged!.State);
        Assert.Equal(1, publisher.Count("debit-command"));
        Assert.Equal(0, publisher.Count("credit-command"));
        Assert.Single(await repository.GetTransitionsAsync(created.Transfer.TransferId, CancellationToken.None));
    }

    [Fact]
    public async Task Simulated_credit_timeout_issues_one_refund_command()
    {
        var repository = new InMemoryTransferSagaRepository();
        var publisher = new RecordingSagaPublisher();
        var clock = new FakeClock(Now);
        var created = await CreateHandler(repository, publisher, clock)
            .HandleAsync(Request("timeout-key", SagaSimulationModes.CreditTimeout), CancellationToken.None);
        var outcomeHandler = new ProcessSagaOutcomeHandler(
            repository,
            publisher,
            new NoopTransactionBoundary(),
            clock,
            Options());
        await outcomeHandler.HandleFundsDebitedAsync(
            new FundsDebitedPayload(
                created.Transfer.TransferId,
                BankIds.BankA,
                created.Transfer.SenderAccountId,
                created.Transfer.SenderUserId,
                created.Transfer.Amount,
                75m),
            Message("debit-event"),
            CancellationToken.None);
        clock.UtcNow = Now.AddSeconds(9);
        var timeoutHandler = new ProcessExpiredSagasHandler(
            repository,
            publisher,
            new NoopTransactionBoundary(),
            clock,
            Options());

        var first = await timeoutHandler.HandleAsync(CancellationToken.None);
        var duplicate = await timeoutHandler.HandleAsync(CancellationToken.None);

        Assert.Equal(1, first);
        Assert.Equal(0, duplicate);
        Assert.Equal(1, publisher.Count("refund-command"));
        var saga = await repository.GetAsync(created.Transfer.TransferId, CancellationToken.None);
        Assert.Equal(TransferSagaState.CompensationPending, saga!.State);
    }

    private static StartedTransferSaga StartSaga(TransferSimulationMode mode = TransferSimulationMode.Normal) =>
        TransferSaga.Start(
            "transfer-1",
            "key-1",
            "sender",
            "sender_bank-a",
            BankIds.BankA,
            "recipient",
            "recipient_bank-b",
            BankIds.BankB,
            new TransferAmount(25m),
            mode,
            Now,
            TimeSpan.FromSeconds(30),
            Message("request"));

    private static CreateTransferHandler CreateHandler(
        ITransferSagaRepository repository,
        RecordingSagaPublisher publisher,
        FakeClock? clock = null) =>
        new(
            repository,
            publisher,
            new NoopTransactionBoundary(),
            clock ?? new FakeClock(Now),
            new AllowSimulationPolicy(),
            Options());

    private static SagaOptions Options() => new()
    {
        StepTimeout = TimeSpan.FromSeconds(30),
        SimulatedCreditTimeout = TimeSpan.FromSeconds(8),
        CompensationTimeout = TimeSpan.FromSeconds(30),
        TimeoutBatchSize = 50
    };

    private static RealtimePix.Transaction.Application.PixTransferRequest Request(
        string key,
        string simulationMode = SagaSimulationModes.Normal) =>
        new(
            key,
            "sender",
            "sender_bank-a",
            "recipient",
            "recipient_bank-b",
            25m,
            BankIds.BankA,
            BankIds.BankB,
            simulationMode);

    private static SagaMessageMetadata Message(string id) =>
        new(id, id, "transfer-1", null);

    private sealed class FakeClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }

    private sealed class AllowSimulationPolicy : ISagaSimulationPolicy
    {
        public void EnsureAllowed(TransferSimulationMode mode)
        {
        }
    }

    private sealed class RecordingSagaPublisher : ISagaMessagePublisher
    {
        private readonly List<string> _messages = [];

        public int Count(string message) => _messages.Count(item => item == message);

        public Task PublishDebitCommandAsync(TransferSaga saga, SagaTransition transition, CancellationToken cancellationToken) => Add("debit-command");
        public Task PublishCreditCommandAsync(TransferSaga saga, SagaTransition transition, CancellationToken cancellationToken) => Add("credit-command");
        public Task PublishRefundCommandAsync(TransferSaga saga, SagaTransition transition, CancellationToken cancellationToken) => Add("refund-command");
        public Task PublishLegacyRequestedAsync(TransferSaga saga, SagaTransition transition, CancellationToken cancellationToken) => Add("legacy-requested");
        public Task PublishTransitionAsync(TransferSaga saga, SagaTransition transition, CancellationToken cancellationToken) => Add("transition");
        public Task PublishCompletedAsync(TransferSaga saga, SagaTransition transition, CancellationToken cancellationToken) => Add("completed");
        public Task PublishFailedAsync(TransferSaga saga, SagaTransition transition, bool requiresManualIntervention, CancellationToken cancellationToken) => Add("failed");
        public Task PublishCompensatedAsync(TransferSaga saga, SagaTransition transition, CancellationToken cancellationToken) => Add("compensated");
        public Task PublishTimedOutAsync(TransferSaga saga, SagaTransition transition, string timedOutState, CancellationToken cancellationToken) => Add("timed-out");

        private Task Add(string message)
        {
            lock (_messages)
            {
                _messages.Add(message);
            }

            return Task.CompletedTask;
        }
    }
}
