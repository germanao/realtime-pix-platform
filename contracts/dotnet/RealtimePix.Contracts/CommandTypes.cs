namespace RealtimePix.Contracts;

public static class CommandTypes
{
    public const string DebitFunds = "DebitFunds.v1";
    public const string CreditFunds = "CreditFunds.v1";
    public const string RefundFunds = "RefundFunds.v1";

    public static readonly string[] All = [DebitFunds, CreditFunds, RefundFunds];
}

public static class BankIds
{
    public const string BankA = "bank-a";
    public const string BankB = "bank-b";

    public static readonly string[] All = [BankA, BankB];

    public static string QueueName(string bankId) => bankId switch
    {
        BankA => "bank-a-commands",
        BankB => "bank-b-commands",
        _ => throw new ArgumentOutOfRangeException(nameof(bankId), bankId, "Unknown bank identifier.")
    };
}

public static class SagaSimulationModes
{
    public const string Normal = "normal";
    public const string CreditRejected = "credit_rejected";
    public const string CreditTimeout = "credit_timeout";
    public const string RefundRejectedTest = "refund_rejected_test";

    public static readonly string[] All = [Normal, CreditRejected, CreditTimeout, RefundRejectedTest];
}

public static class TransferSagaStates
{
    public const string DebitPending = "debit_pending";
    public const string CreditPending = "credit_pending";
    public const string Completed = "completed";
    public const string CompensationPending = "compensation_pending";
    public const string Compensated = "compensated";
    public const string Failed = "failed";
    public const string ManualIntervention = "manual_intervention";
}
