namespace RealtimePix.Transaction.Domain;

public enum TransferSagaState
{
    DebitPending,
    CreditPending,
    Completed,
    CompensationPending,
    Compensated,
    Failed,
    ManualIntervention
}

public enum TransferSimulationMode
{
    Normal,
    CreditRejected,
    CreditTimeout,
    RefundRejectedTest
}

public sealed record SagaMessageMetadata(
    string MessageId,
    string MessageType,
    string CorrelationId,
    string? CausationId);

public sealed record SagaTransition(
    string TransitionId,
    string TransferId,
    TransferSagaState? PreviousState,
    TransferSagaState NextState,
    int PreviousVersion,
    int NextVersion,
    string TriggeringMessageId,
    string TriggeringMessageType,
    string CorrelationId,
    string? CausationId,
    string? Reason,
    DateTimeOffset RecordedAt);

public sealed record StartedTransferSaga(TransferSaga Saga, SagaTransition Transition);

public sealed class TransferSaga
{
    private TransferSaga()
    {
    }

    public string TransferId { get; private set; } = string.Empty;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string SenderUserId { get; private set; } = string.Empty;
    public string SenderAccountId { get; private set; } = string.Empty;
    public string SenderBankId { get; private set; } = string.Empty;
    public string RecipientUserId { get; private set; } = string.Empty;
    public string RecipientAccountId { get; private set; } = string.Empty;
    public string RecipientBankId { get; private set; } = string.Empty;
    public TransferAmount Amount { get; private set; }
    public TransferSimulationMode SimulationMode { get; private set; }
    public TransferSagaState State { get; private set; }
    public int Version { get; private set; }
    public string? FailureCode { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset DeadlineAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset? CompensationStartedAt { get; private set; }
    public DateTimeOffset? CompensatedAt { get; private set; }

    public bool IsTerminal => State is
        TransferSagaState.Completed or
        TransferSagaState.Compensated or
        TransferSagaState.Failed or
        TransferSagaState.ManualIntervention;

    public static StartedTransferSaga Start(
        string transferId,
        string idempotencyKey,
        string senderUserId,
        string senderAccountId,
        string senderBankId,
        string recipientUserId,
        string recipientAccountId,
        string recipientBankId,
        TransferAmount amount,
        TransferSimulationMode simulationMode,
        DateTimeOffset now,
        TimeSpan debitDeadline,
        SagaMessageMetadata metadata)
    {
        ValidateRequired(transferId, nameof(transferId));
        ValidateRequired(idempotencyKey, nameof(idempotencyKey));
        ValidateRequired(senderUserId, nameof(senderUserId));
        ValidateRequired(senderAccountId, nameof(senderAccountId));
        ValidateRequired(senderBankId, nameof(senderBankId));
        ValidateRequired(recipientUserId, nameof(recipientUserId));
        ValidateRequired(recipientAccountId, nameof(recipientAccountId));
        ValidateRequired(recipientBankId, nameof(recipientBankId));
        if (debitDeadline <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(debitDeadline));
        }

        var saga = new TransferSaga
        {
            TransferId = transferId,
            IdempotencyKey = idempotencyKey,
            SenderUserId = senderUserId,
            SenderAccountId = senderAccountId,
            SenderBankId = senderBankId,
            RecipientUserId = recipientUserId,
            RecipientAccountId = recipientAccountId,
            RecipientBankId = recipientBankId,
            Amount = amount,
            SimulationMode = simulationMode,
            State = TransferSagaState.DebitPending,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now,
            DeadlineAt = now.Add(debitDeadline)
        };

        return new StartedTransferSaga(
            saga,
            saga.CreateTransition(
                previousState: null,
                TransferSagaState.DebitPending,
                previousVersion: 0,
                metadata,
                reason: null,
                now));
    }

    public static TransferSaga Rehydrate(
        string transferId,
        string idempotencyKey,
        string senderUserId,
        string senderAccountId,
        string senderBankId,
        string recipientUserId,
        string recipientAccountId,
        string recipientBankId,
        decimal amount,
        TransferSimulationMode simulationMode,
        TransferSagaState state,
        int version,
        string? failureCode,
        string? failureReason,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        DateTimeOffset deadlineAt,
        DateTimeOffset? completedAt,
        DateTimeOffset? compensationStartedAt,
        DateTimeOffset? compensatedAt) =>
        new()
        {
            TransferId = transferId,
            IdempotencyKey = idempotencyKey,
            SenderUserId = senderUserId,
            SenderAccountId = senderAccountId,
            SenderBankId = senderBankId,
            RecipientUserId = recipientUserId,
            RecipientAccountId = recipientAccountId,
            RecipientBankId = recipientBankId,
            Amount = new TransferAmount(amount),
            SimulationMode = simulationMode,
            State = state,
            Version = version,
            FailureCode = failureCode,
            FailureReason = failureReason,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            DeadlineAt = deadlineAt,
            CompletedAt = completedAt,
            CompensationStartedAt = compensationStartedAt,
            CompensatedAt = compensatedAt
        };

    public SagaTransition RecordDebitSucceeded(
        SagaMessageMetadata metadata,
        DateTimeOffset now,
        TimeSpan creditDeadline)
    {
        EnsureState(TransferSagaState.DebitPending);
        DeadlineAt = now.Add(creditDeadline);
        FailureCode = null;
        FailureReason = null;
        return TransitionTo(TransferSagaState.CreditPending, metadata, null, now);
    }

    public SagaTransition RecordDebitRejected(
        SagaMessageMetadata metadata,
        string reason,
        DateTimeOffset now)
    {
        EnsureState(TransferSagaState.DebitPending);
        FailureCode = "debit_rejected";
        FailureReason = reason;
        CompletedAt = now;
        return TransitionTo(TransferSagaState.Failed, metadata, reason, now);
    }

    public SagaTransition RecordDebitTimeout(SagaMessageMetadata metadata, DateTimeOffset now)
    {
        EnsureState(TransferSagaState.DebitPending);
        FailureCode = "debit_timeout";
        FailureReason = "The debit command did not finish before the Saga deadline.";
        CompletedAt = now;
        return TransitionTo(TransferSagaState.Failed, metadata, FailureReason, now);
    }

    public SagaTransition RecordCreditSucceeded(SagaMessageMetadata metadata, DateTimeOffset now)
    {
        EnsureState(TransferSagaState.CreditPending);
        FailureCode = null;
        FailureReason = null;
        CompletedAt = now;
        return TransitionTo(TransferSagaState.Completed, metadata, null, now);
    }

    public SagaTransition RecordCreditRejected(
        SagaMessageMetadata metadata,
        string reason,
        DateTimeOffset now,
        TimeSpan compensationDeadline)
    {
        EnsureState(TransferSagaState.CreditPending);
        FailureCode = "credit_rejected";
        FailureReason = reason;
        CompensationStartedAt = now;
        DeadlineAt = now.Add(compensationDeadline);
        return TransitionTo(TransferSagaState.CompensationPending, metadata, reason, now);
    }

    public SagaTransition RecordCreditTimeout(
        SagaMessageMetadata metadata,
        DateTimeOffset now,
        TimeSpan compensationDeadline)
    {
        EnsureState(TransferSagaState.CreditPending);
        FailureCode = "credit_timeout";
        FailureReason = "The credit command did not finish before the Saga deadline.";
        CompensationStartedAt = now;
        DeadlineAt = now.Add(compensationDeadline);
        return TransitionTo(TransferSagaState.CompensationPending, metadata, FailureReason, now);
    }

    public SagaTransition RecordRefundSucceeded(SagaMessageMetadata metadata, DateTimeOffset now)
    {
        EnsureState(TransferSagaState.CompensationPending);
        CompensatedAt = now;
        CompletedAt = now;
        return TransitionTo(TransferSagaState.Compensated, metadata, FailureReason, now);
    }

    public SagaTransition RecordRefundRejected(
        SagaMessageMetadata metadata,
        string reason,
        DateTimeOffset now)
    {
        EnsureState(TransferSagaState.CompensationPending);
        FailureCode = "refund_rejected";
        FailureReason = reason;
        CompletedAt = now;
        return TransitionTo(TransferSagaState.ManualIntervention, metadata, reason, now);
    }

    public SagaTransition RecordCompensationTimeout(SagaMessageMetadata metadata, DateTimeOffset now)
    {
        EnsureState(TransferSagaState.CompensationPending);
        FailureCode = "refund_timeout";
        FailureReason = "The refund command did not finish before the Saga deadline.";
        CompletedAt = now;
        return TransitionTo(TransferSagaState.ManualIntervention, metadata, FailureReason, now);
    }

    public void EnsureSenderOutcome(string bankId, string accountId, string userId, decimal amount) =>
        EnsureOutcomeMatches(SenderBankId, SenderAccountId, SenderUserId, bankId, accountId, userId, amount);

    public void EnsureRecipientOutcome(string bankId, string accountId, string userId, decimal amount) =>
        EnsureOutcomeMatches(RecipientBankId, RecipientAccountId, RecipientUserId, bankId, accountId, userId, amount);

    private SagaTransition TransitionTo(
        TransferSagaState nextState,
        SagaMessageMetadata metadata,
        string? reason,
        DateTimeOffset now)
    {
        var previousState = State;
        var previousVersion = Version;
        State = nextState;
        Version++;
        UpdatedAt = now;
        return CreateTransition(previousState, nextState, previousVersion, metadata, reason, now);
    }

    private SagaTransition CreateTransition(
        TransferSagaState? previousState,
        TransferSagaState nextState,
        int previousVersion,
        SagaMessageMetadata metadata,
        string? reason,
        DateTimeOffset now) =>
        new(
            Guid.NewGuid().ToString("N"),
            TransferId,
            previousState,
            nextState,
            previousVersion,
            Version,
            metadata.MessageId,
            metadata.MessageType,
            metadata.CorrelationId,
            metadata.CausationId,
            reason,
            now);

    private void EnsureState(TransferSagaState expected)
    {
        if (State != expected)
        {
            throw new InvalidSagaTransitionException(TransferId, State, expected);
        }
    }

    private void EnsureOutcomeMatches(
        string expectedBankId,
        string expectedAccountId,
        string expectedUserId,
        string bankId,
        string accountId,
        string userId,
        decimal amount)
    {
        if (!string.Equals(expectedBankId, bankId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(expectedAccountId, accountId, StringComparison.Ordinal) ||
            !string.Equals(expectedUserId, userId, StringComparison.Ordinal) ||
            Amount != new TransferAmount(amount))
        {
            throw new InvalidSagaOutcomeException(TransferId);
        }
    }

    private static void ValidateRequired(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A non-empty value is required.", parameterName);
        }
    }
}

public sealed class InvalidSagaTransitionException(
    string transferId,
    TransferSagaState currentState,
    TransferSagaState expectedState) : InvalidOperationException(
        $"Transfer '{transferId}' is in state '{currentState}' but transition requires '{expectedState}'.");

public sealed class InvalidSagaOutcomeException(string transferId) : InvalidOperationException(
    $"A bank outcome did not match the immutable participants or amount of transfer '{transferId}'.");
