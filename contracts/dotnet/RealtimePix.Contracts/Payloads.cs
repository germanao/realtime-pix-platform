namespace RealtimePix.Contracts;

public sealed record AnonymousUserJoinedPayload(
    string UserId,
    string DisplayName,
    string ClientId,
    bool IsBot);

public sealed record UserPresenceChangedPayload(
    string UserId,
    string DisplayName,
    bool IsOnline,
    bool IsBot,
    DateTimeOffset LastSeenAt);

public sealed record AccountCreatedPayload(
    string AccountId,
    string UserId,
    string BankName,
    decimal Balance,
    string BankId = "");

public sealed record FundsDepositedPayload(
    string LedgerEntryId,
    string AccountId,
    string UserId,
    decimal Amount,
    decimal NewBalance,
    string Reason,
    string BankId = "");

public sealed record PixTransferRequestedPayload(
    string TransferId,
    string IdempotencyKey,
    string SenderUserId,
    string SenderAccountId,
    string RecipientUserId,
    string RecipientAccountId,
    decimal Amount);

public sealed record PixDebitSucceededPayload(
    string TransferId,
    string SenderUserId,
    string SenderAccountId,
    decimal Amount,
    decimal NewBalance);

public sealed record PixDebitFailedPayload(
    string TransferId,
    string SenderUserId,
    string SenderAccountId,
    decimal Amount,
    string Reason);

public sealed record PixCreditSucceededPayload(
    string TransferId,
    string RecipientUserId,
    string RecipientAccountId,
    decimal Amount,
    decimal NewBalance);

public sealed record PixTransferCompletedPayload(
    string TransferId,
    string SenderUserId,
    string RecipientUserId,
    decimal Amount,
    DateTimeOffset CompletedAt);

public sealed record PixTransferFailedPayload(
    string TransferId,
    string SenderUserId,
    string RecipientUserId,
    decimal Amount,
    string Reason,
    DateTimeOffset FailedAt);

public sealed record ArchitectureFlowStepRecordedPayload(
    string StepId,
    string? TransferId,
    string EventType,
    string Stage,
    string Title,
    string Detail,
    DateTimeOffset RecordedAt);

public sealed record DebitFundsPayload(
    string TransferId,
    string SenderUserId,
    string SenderAccountId,
    string SenderBankId,
    string RecipientUserId,
    string RecipientAccountId,
    string RecipientBankId,
    decimal Amount,
    string SimulationMode);

public sealed record CreditFundsPayload(
    string TransferId,
    string RecipientUserId,
    string RecipientAccountId,
    string RecipientBankId,
    string SenderUserId,
    string SenderAccountId,
    string SenderBankId,
    decimal Amount,
    string SimulationMode);

public sealed record RefundFundsPayload(
    string TransferId,
    string SenderUserId,
    string SenderAccountId,
    string SenderBankId,
    decimal Amount,
    string Reason,
    string SimulationMode = SagaSimulationModes.Normal);

public sealed record FundsDebitedPayload(
    string TransferId,
    string BankId,
    string AccountId,
    string UserId,
    decimal Amount,
    decimal NewBalance);

public sealed record FundsDebitRejectedPayload(
    string TransferId,
    string BankId,
    string AccountId,
    string UserId,
    decimal Amount,
    string Reason);

public sealed record FundsCreditedPayload(
    string TransferId,
    string BankId,
    string AccountId,
    string UserId,
    decimal Amount,
    decimal NewBalance);

public sealed record FundsCreditRejectedPayload(
    string TransferId,
    string BankId,
    string AccountId,
    string UserId,
    decimal Amount,
    string Reason);

public sealed record FundsRefundedPayload(
    string TransferId,
    string BankId,
    string AccountId,
    string UserId,
    decimal Amount,
    decimal NewBalance);

public sealed record FundsRefundRejectedPayload(
    string TransferId,
    string BankId,
    string AccountId,
    string UserId,
    decimal Amount,
    string Reason);

public sealed record PixTransferCompletedV2Payload(
    string TransferId,
    string SenderUserId,
    string RecipientUserId,
    decimal Amount,
    DateTimeOffset CompletedAt);

public sealed record PixTransferCompensatedPayload(
    string TransferId,
    string SenderUserId,
    string RecipientUserId,
    decimal Amount,
    string Reason,
    DateTimeOffset CompensatedAt);

public sealed record PixTransferFailedV2Payload(
    string TransferId,
    string SenderUserId,
    string RecipientUserId,
    decimal Amount,
    string Reason,
    bool RequiresManualIntervention,
    DateTimeOffset FailedAt);

public sealed record PixSagaTimedOutPayload(
    string TransferId,
    string TimedOutState,
    DateTimeOffset TimedOutAt);

public sealed record SagaTransitionRecordedPayload(
    string TransitionId,
    string TransferId,
    string? PreviousState,
    string NextState,
    string TriggeringMessageType,
    string? Reason,
    DateTimeOffset RecordedAt);
