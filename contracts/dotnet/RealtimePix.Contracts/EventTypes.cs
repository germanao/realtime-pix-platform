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
        ArchitectureFlowStepRecorded
    ];
}

