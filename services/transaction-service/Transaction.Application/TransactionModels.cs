using RealtimePix.Transaction.Domain;

namespace RealtimePix.Transaction.Application;

public sealed record PixTransferRequest(
    string? IdempotencyKey,
    string SenderUserId,
    string SenderAccountId,
    string RecipientUserId,
    string RecipientAccountId,
    decimal Amount,
    string? SenderBankId = null,
    string? RecipientBankId = null,
    string? SimulationMode = null);

public sealed record TransferResponse(
    string TransferId,
    string IdempotencyKey,
    string SenderUserId,
    string SenderAccountId,
    string SenderBankId,
    string RecipientUserId,
    string RecipientAccountId,
    string RecipientBankId,
    decimal Amount,
    string Status,
    string SagaState,
    string CurrentStep,
    string CompensationState,
    string? FailureCode,
    string? FailureReason,
    int Version,
    string SimulationMode,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset DeadlineAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? CompensationStartedAt,
    DateTimeOffset? CompensatedAt);

public sealed record SagaTransitionResponse(
    string TransitionId,
    string TransferId,
    string? PreviousState,
    string NextState,
    int PreviousVersion,
    int NextVersion,
    string TriggeringMessageId,
    string TriggeringMessageType,
    string CorrelationId,
    string? CausationId,
    string? Reason,
    DateTimeOffset RecordedAt);

public sealed record CreateTransferResult(bool IsNew, TransferResponse Transfer);

public sealed record SagaProcessingResult(bool Changed, TransferResponse? Transfer);

public sealed class SagaOptions
{
    public TimeSpan StepTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan SimulatedCreditTimeout { get; set; } = TimeSpan.FromSeconds(8);

    public TimeSpan CompensationTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public int TimeoutBatchSize { get; set; } = 50;
}

public interface ITransferSagaRepository
{
    Task AddAsync(TransferSaga saga, SagaTransition transition, CancellationToken cancellationToken);

    Task<TransferSaga?> GetAsync(string transferId, CancellationToken cancellationToken);

    Task<TransferSaga?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken);

    Task SaveTransitionAsync(
        TransferSaga saga,
        int expectedVersion,
        SagaTransition transition,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<TransferSaga>> GetExpiredAsync(
        DateTimeOffset now,
        int take,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<SagaTransition>> GetTransitionsAsync(
        string transferId,
        CancellationToken cancellationToken);
}

public interface ISagaMessagePublisher
{
    Task PublishDebitCommandAsync(TransferSaga saga, SagaTransition transition, CancellationToken cancellationToken);

    Task PublishCreditCommandAsync(TransferSaga saga, SagaTransition transition, CancellationToken cancellationToken);

    Task PublishRefundCommandAsync(TransferSaga saga, SagaTransition transition, CancellationToken cancellationToken);

    Task PublishLegacyRequestedAsync(TransferSaga saga, SagaTransition transition, CancellationToken cancellationToken);

    Task PublishTransitionAsync(TransferSaga saga, SagaTransition transition, CancellationToken cancellationToken);

    Task PublishCompletedAsync(TransferSaga saga, SagaTransition transition, CancellationToken cancellationToken);

    Task PublishFailedAsync(TransferSaga saga, SagaTransition transition, bool requiresManualIntervention, CancellationToken cancellationToken);

    Task PublishCompensatedAsync(TransferSaga saga, SagaTransition transition, CancellationToken cancellationToken);

    Task PublishTimedOutAsync(TransferSaga saga, SagaTransition transition, string timedOutState, CancellationToken cancellationToken);
}

public interface ITransactionBoundary
{
    Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken);
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public interface ISagaSimulationPolicy
{
    void EnsureAllowed(TransferSimulationMode mode);
}

public sealed record ReadinessResult(bool IsReady, string? Reason = null);

public interface ITransactionReadinessProbe
{
    Task<ReadinessResult> CheckAsync(CancellationToken cancellationToken);
}

public sealed class DuplicateIdempotencyKeyException(string idempotencyKey) : Exception(
    $"A transfer already exists for idempotency key '{idempotencyKey}'.");

public sealed class SagaConcurrencyException(string transferId, int expectedVersion) : Exception(
    $"Transfer '{transferId}' was not at expected version {expectedVersion}.");

public sealed class SagaSimulationNotAllowedException() : Exception(
    "Saga failure simulation is disabled in this environment.");

public static class TransferSagaPresentation
{
    public static TransferResponse ToResponse(TransferSaga saga) =>
        new(
            saga.TransferId,
            saga.IdempotencyKey,
            saga.SenderUserId,
            saga.SenderAccountId,
            saga.SenderBankId,
            saga.RecipientUserId,
            saga.RecipientAccountId,
            saga.RecipientBankId,
            saga.Amount.Value,
            LegacyStatus(saga.State),
            StateName(saga.State),
            CurrentStep(saga.State),
            CompensationState(saga.State),
            saga.FailureCode,
            saga.FailureReason,
            saga.Version,
            SimulationModeName(saga.SimulationMode),
            saga.CreatedAt,
            saga.UpdatedAt,
            saga.DeadlineAt,
            saga.CompletedAt,
            saga.CompensationStartedAt,
            saga.CompensatedAt);

    public static SagaTransitionResponse ToResponse(SagaTransition transition) =>
        new(
            transition.TransitionId,
            transition.TransferId,
            transition.PreviousState is null ? null : StateName(transition.PreviousState.Value),
            StateName(transition.NextState),
            transition.PreviousVersion,
            transition.NextVersion,
            transition.TriggeringMessageId,
            transition.TriggeringMessageType,
            transition.CorrelationId,
            transition.CausationId,
            transition.Reason,
            transition.RecordedAt);

    public static string StateName(TransferSagaState state) => state switch
    {
        TransferSagaState.DebitPending => "debit_pending",
        TransferSagaState.CreditPending => "credit_pending",
        TransferSagaState.Completed => "completed",
        TransferSagaState.CompensationPending => "compensation_pending",
        TransferSagaState.Compensated => "compensated",
        TransferSagaState.Failed => "failed",
        TransferSagaState.ManualIntervention => "manual_intervention",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
    };

    public static string SimulationModeName(TransferSimulationMode mode) => mode switch
    {
        TransferSimulationMode.Normal => "normal",
        TransferSimulationMode.CreditRejected => "credit_rejected",
        TransferSimulationMode.CreditTimeout => "credit_timeout",
        TransferSimulationMode.RefundRejectedTest => "refund_rejected_test",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
    };

    private static string LegacyStatus(TransferSagaState state) => state switch
    {
        TransferSagaState.DebitPending => "requested",
        TransferSagaState.CreditPending or TransferSagaState.CompensationPending => "debited",
        TransferSagaState.Completed => "completed",
        _ => "failed"
    };

    private static string CurrentStep(TransferSagaState state) => state switch
    {
        TransferSagaState.DebitPending => "debit",
        TransferSagaState.CreditPending => "credit",
        TransferSagaState.CompensationPending => "refund",
        TransferSagaState.Completed => "complete",
        TransferSagaState.Compensated => "compensated",
        TransferSagaState.Failed => "failed",
        TransferSagaState.ManualIntervention => "manual_intervention",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
    };

    private static string CompensationState(TransferSagaState state) => state switch
    {
        TransferSagaState.CompensationPending => "pending",
        TransferSagaState.Compensated => "completed",
        TransferSagaState.ManualIntervention => "manual_intervention",
        _ => "not_required"
    };
}
