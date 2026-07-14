using RealtimePix.Contracts;
using RealtimePix.Transaction.Domain;

namespace RealtimePix.Transaction.Application;

public sealed class CreateTransferHandler(
    ITransferSagaRepository repository,
    ISagaMessagePublisher publisher,
    ITransactionBoundary transaction,
    IClock clock,
    ISagaSimulationPolicy simulationPolicy,
    SagaOptions options)
{
    public async Task<CreateTransferResult> HandleAsync(PixTransferRequest request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SenderUserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SenderAccountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RecipientUserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RecipientAccountId);

        var idempotencyKey = string.IsNullOrWhiteSpace(request.IdempotencyKey)
            ? Guid.NewGuid().ToString("N")
            : request.IdempotencyKey.Trim();
        var existing = await repository.GetByIdempotencyKeyAsync(idempotencyKey, cancellationToken);
        if (existing is not null)
        {
            return new CreateTransferResult(false, TransferSagaPresentation.ToResponse(existing));
        }

        var senderBankId = ResolveBankId(request.SenderBankId, request.SenderAccountId);
        var recipientBankId = ResolveBankId(request.RecipientBankId, request.RecipientAccountId);
        var simulationMode = ParseSimulationMode(request.SimulationMode);
        simulationPolicy.EnsureAllowed(simulationMode);
        var now = clock.UtcNow;
        var transferId = Guid.NewGuid().ToString("N");
        var metadata = new SagaMessageMetadata(
            transferId,
            EventTypes.PixTransferRequested,
            transferId,
            CausationId: null);
        var started = TransferSaga.Start(
            transferId,
            idempotencyKey,
            request.SenderUserId,
            request.SenderAccountId,
            senderBankId,
            request.RecipientUserId,
            request.RecipientAccountId,
            recipientBankId,
            new TransferAmount(request.Amount),
            simulationMode,
            now,
            options.StepTimeout,
            metadata);

        try
        {
            await transaction.ExecuteAsync(async innerCancellationToken =>
            {
                await repository.AddAsync(started.Saga, started.Transition, innerCancellationToken);
                await publisher.PublishTransitionAsync(started.Saga, started.Transition, innerCancellationToken);
                await publisher.PublishLegacyRequestedAsync(started.Saga, started.Transition, innerCancellationToken);
                await publisher.PublishDebitCommandAsync(started.Saga, started.Transition, innerCancellationToken);
            }, cancellationToken);
        }
        catch (DuplicateIdempotencyKeyException ex)
        {
            var duplicate = await repository.GetByIdempotencyKeyAsync(idempotencyKey, cancellationToken)
                ?? throw new InvalidOperationException(
                    "The idempotency key conflicted, but the existing transfer could not be loaded.",
                    ex);
            return new CreateTransferResult(false, TransferSagaPresentation.ToResponse(duplicate));
        }

        return new CreateTransferResult(true, TransferSagaPresentation.ToResponse(started.Saga));
    }

    private static string ResolveBankId(string? explicitBankId, string accountId)
    {
        if (!string.IsNullOrWhiteSpace(explicitBankId))
        {
            if (!BankIds.All.Contains(explicitBankId, StringComparer.OrdinalIgnoreCase))
            {
                throw new ArgumentOutOfRangeException(nameof(explicitBankId), explicitBankId, "Unknown bank identifier.");
            }

            return explicitBankId.ToLowerInvariant();
        }

        return BankIds.All.FirstOrDefault(bankId => accountId.EndsWith($"_{bankId}", StringComparison.OrdinalIgnoreCase))
            ?? (accountId.EndsWith("_bank-b", StringComparison.OrdinalIgnoreCase) ? BankIds.BankB : BankIds.BankA);
    }

    private static TransferSimulationMode ParseSimulationMode(string? value) => value switch
    {
        null or "" or SagaSimulationModes.Normal => TransferSimulationMode.Normal,
        SagaSimulationModes.CreditRejected => TransferSimulationMode.CreditRejected,
        SagaSimulationModes.CreditTimeout => TransferSimulationMode.CreditTimeout,
        SagaSimulationModes.RefundRejectedTest => TransferSimulationMode.RefundRejectedTest,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown Saga simulation mode.")
    };
}

public sealed class GetTransferHandler(ITransferSagaRepository repository)
{
    public async Task<TransferResponse?> HandleAsync(string transferId, CancellationToken cancellationToken)
    {
        var saga = await repository.GetAsync(transferId, cancellationToken);
        return saga is null ? null : TransferSagaPresentation.ToResponse(saga);
    }
}

public sealed class GetSagaTransitionsHandler(ITransferSagaRepository repository)
{
    public async Task<IReadOnlyCollection<SagaTransitionResponse>> HandleAsync(
        string transferId,
        CancellationToken cancellationToken)
    {
        var transitions = await repository.GetTransitionsAsync(transferId, cancellationToken);
        return transitions.Select(TransferSagaPresentation.ToResponse).ToArray();
    }
}

public sealed class ProcessSagaOutcomeHandler(
    ITransferSagaRepository repository,
    ISagaMessagePublisher publisher,
    ITransactionBoundary transaction,
    IClock clock,
    SagaOptions options)
{
    public Task<SagaProcessingResult> HandleFundsDebitedAsync(
        FundsDebitedPayload payload,
        SagaMessageMetadata metadata,
        CancellationToken cancellationToken) =>
        ProcessAsync(
            payload.TransferId,
            TransferSagaState.DebitPending,
            saga => saga.EnsureSenderOutcome(payload.BankId, payload.AccountId, payload.UserId, payload.Amount),
            saga => saga.RecordDebitSucceeded(
                metadata,
                clock.UtcNow,
                saga.SimulationMode == TransferSimulationMode.CreditTimeout
                    ? options.SimulatedCreditTimeout
                    : options.StepTimeout),
            (saga, transition, token) => publisher.PublishCreditCommandAsync(saga, transition, token),
            cancellationToken);

    public Task<SagaProcessingResult> HandleDebitRejectedAsync(
        FundsDebitRejectedPayload payload,
        SagaMessageMetadata metadata,
        CancellationToken cancellationToken) =>
        ProcessAsync(
            payload.TransferId,
            TransferSagaState.DebitPending,
            saga => saga.EnsureSenderOutcome(payload.BankId, payload.AccountId, payload.UserId, payload.Amount),
            saga => saga.RecordDebitRejected(metadata, payload.Reason, clock.UtcNow),
            (saga, transition, token) => publisher.PublishFailedAsync(saga, transition, false, token),
            cancellationToken);

    public Task<SagaProcessingResult> HandleFundsCreditedAsync(
        FundsCreditedPayload payload,
        SagaMessageMetadata metadata,
        CancellationToken cancellationToken) =>
        ProcessAsync(
            payload.TransferId,
            TransferSagaState.CreditPending,
            saga => saga.EnsureRecipientOutcome(payload.BankId, payload.AccountId, payload.UserId, payload.Amount),
            saga => saga.RecordCreditSucceeded(metadata, clock.UtcNow),
            (saga, transition, token) => publisher.PublishCompletedAsync(saga, transition, token),
            cancellationToken);

    public Task<SagaProcessingResult> HandleCreditRejectedAsync(
        FundsCreditRejectedPayload payload,
        SagaMessageMetadata metadata,
        CancellationToken cancellationToken) =>
        ProcessAsync(
            payload.TransferId,
            TransferSagaState.CreditPending,
            saga => saga.EnsureRecipientOutcome(payload.BankId, payload.AccountId, payload.UserId, payload.Amount),
            saga => saga.RecordCreditRejected(metadata, payload.Reason, clock.UtcNow, options.CompensationTimeout),
            (saga, transition, token) => publisher.PublishRefundCommandAsync(saga, transition, token),
            cancellationToken);

    public Task<SagaProcessingResult> HandleFundsRefundedAsync(
        FundsRefundedPayload payload,
        SagaMessageMetadata metadata,
        CancellationToken cancellationToken) =>
        ProcessAsync(
            payload.TransferId,
            TransferSagaState.CompensationPending,
            saga => saga.EnsureSenderOutcome(payload.BankId, payload.AccountId, payload.UserId, payload.Amount),
            saga => saga.RecordRefundSucceeded(metadata, clock.UtcNow),
            (saga, transition, token) => publisher.PublishCompensatedAsync(saga, transition, token),
            cancellationToken);

    public Task<SagaProcessingResult> HandleRefundRejectedAsync(
        FundsRefundRejectedPayload payload,
        SagaMessageMetadata metadata,
        CancellationToken cancellationToken) =>
        ProcessAsync(
            payload.TransferId,
            TransferSagaState.CompensationPending,
            saga => saga.EnsureSenderOutcome(payload.BankId, payload.AccountId, payload.UserId, payload.Amount),
            saga => saga.RecordRefundRejected(metadata, payload.Reason, clock.UtcNow),
            (saga, transition, token) => publisher.PublishFailedAsync(saga, transition, true, token),
            cancellationToken);

    private async Task<SagaProcessingResult> ProcessAsync(
        string transferId,
        TransferSagaState expectedState,
        Action<TransferSaga> validateOutcome,
        Func<TransferSaga, SagaTransition> transitionFactory,
        Func<TransferSaga, SagaTransition, CancellationToken, Task> publishFollowUp,
        CancellationToken cancellationToken)
    {
        TransferSaga? result = null;
        var changed = false;
        await transaction.ExecuteAsync(async innerCancellationToken =>
        {
            var saga = await repository.GetAsync(transferId, innerCancellationToken);
            result = saga;
            if (saga is null || saga.State != expectedState)
            {
                return;
            }

            validateOutcome(saga);
            var expectedVersion = saga.Version;
            var transition = transitionFactory(saga);
            await repository.SaveTransitionAsync(saga, expectedVersion, transition, innerCancellationToken);
            await publisher.PublishTransitionAsync(saga, transition, innerCancellationToken);
            await publishFollowUp(saga, transition, innerCancellationToken);
            result = saga;
            changed = true;
        }, cancellationToken);

        return new SagaProcessingResult(changed, result is null ? null : TransferSagaPresentation.ToResponse(result));
    }
}

public sealed class ProcessExpiredSagasHandler(
    ITransferSagaRepository repository,
    ISagaMessagePublisher publisher,
    ITransactionBoundary transaction,
    IClock clock,
    SagaOptions options)
{
    public async Task<int> HandleAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var expired = await repository.GetExpiredAsync(now, options.TimeoutBatchSize, cancellationToken);
        var changed = 0;
        foreach (var candidate in expired)
        {
            await transaction.ExecuteAsync(async innerCancellationToken =>
            {
                var saga = await repository.GetAsync(candidate.TransferId, innerCancellationToken);
                if (saga is null || saga.IsTerminal || saga.DeadlineAt > now)
                {
                    return;
                }

                var timedOutState = TransferSagaPresentation.StateName(saga.State);
                var metadata = new SagaMessageMetadata(
                    $"timeout-{saga.TransferId}-{saga.Version}",
                    EventTypes.PixSagaTimedOut,
                    saga.TransferId,
                    CausationId: null);
                var expectedVersion = saga.Version;
                SagaTransition transition;
                Func<CancellationToken, Task> followUp;
                switch (saga.State)
                {
                    case TransferSagaState.DebitPending:
                        transition = saga.RecordDebitTimeout(metadata, now);
                        followUp = token => publisher.PublishFailedAsync(saga, transition, false, token);
                        break;
                    case TransferSagaState.CreditPending:
                        transition = saga.RecordCreditTimeout(metadata, now, options.CompensationTimeout);
                        followUp = token => publisher.PublishRefundCommandAsync(saga, transition, token);
                        break;
                    case TransferSagaState.CompensationPending:
                        transition = saga.RecordCompensationTimeout(metadata, now);
                        followUp = token => publisher.PublishFailedAsync(saga, transition, true, token);
                        break;
                    default:
                        return;
                }

                await repository.SaveTransitionAsync(saga, expectedVersion, transition, innerCancellationToken);
                await publisher.PublishTransitionAsync(saga, transition, innerCancellationToken);
                await publisher.PublishTimedOutAsync(saga, transition, timedOutState, innerCancellationToken);
                await followUp(innerCancellationToken);
                changed++;
            }, cancellationToken);
        }

        return changed;
    }
}
