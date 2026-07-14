namespace RealtimePix.Transaction.Domain;

public readonly record struct TransferAmount
{
    public TransferAmount(decimal value)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Transfer amount must be positive.");
        }

        if (decimal.Round(value, 2) != value)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Transfer amount supports at most two decimal places.");
        }

        Value = value;
    }

    public decimal Value { get; }
}
