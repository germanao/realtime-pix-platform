namespace RealtimePix.Contracts;

public static class EventTypes
{
    public const string AnonymousUserJoined = "AnonymousUserJoined.v1";
    public const string UserPresenceChanged = "UserPresenceChanged.v1";
    public const string AccountCreated = "AccountCreated.v1";
    public const string FundsDeposited = "FundsDeposited.v1";
    public const string PixTransferRequested = "PixTransferRequested.v1";
    public const string PixDebitSucceeded = "PixDebitSucceeded.v1";
    public const string PixDebitFailed = "PixDebitFailed.v1";
    public const string PixCreditSucceeded = "PixCreditSucceeded.v1";
    public const string PixTransferCompleted = "PixTransferCompleted.v1";
    public const string PixTransferFailed = "PixTransferFailed.v1";
    public const string ArchitectureFlowStepRecorded = "ArchitectureFlowStepRecorded.v1";
    public const string FundsDebited = "FundsDebited.v1";
    public const string FundsDebitRejected = "FundsDebitRejected.v1";
    public const string FundsCredited = "FundsCredited.v1";
    public const string FundsCreditRejected = "FundsCreditRejected.v1";
    public const string FundsRefunded = "FundsRefunded.v1";
    public const string FundsRefundRejected = "FundsRefundRejected.v1";
    public const string PixTransferCompletedV2 = "PixTransferCompleted.v2";
    public const string PixTransferFailedV2 = "PixTransferFailed.v2";
    public const string PixTransferCompensated = "PixTransferCompensated.v1";
    public const string PixSagaTimedOut = "PixSagaTimedOut.v1";
    public const string SagaTransitionRecorded = "SagaTransitionRecorded.v1";

    public static readonly string[] All =
    [
        AnonymousUserJoined,
        UserPresenceChanged,
        AccountCreated,
        FundsDeposited,
        PixTransferRequested,
        PixDebitSucceeded,
        PixDebitFailed,
        PixCreditSucceeded,
        PixTransferCompleted,
        PixTransferFailed,
        ArchitectureFlowStepRecorded,
        FundsDebited,
        FundsDebitRejected,
        FundsCredited,
        FundsCreditRejected,
        FundsRefunded,
        FundsRefundRejected,
        PixTransferCompletedV2,
        PixTransferFailedV2,
        PixTransferCompensated,
        PixSagaTimedOut,
        SagaTransitionRecorded
    ];
}
