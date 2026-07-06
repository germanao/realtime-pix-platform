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
    decimal Balance);

public sealed record FundsDepositedPayload(
    string LedgerEntryId,
    string AccountId,
    string UserId,
    decimal Amount,
    decimal NewBalance,
    string Reason);

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

