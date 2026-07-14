namespace RealtimePix.BankLedger.Domain;

public readonly record struct Money
{
    public Money(decimal amount)
    {
        if (decimal.Round(amount, 2) != amount)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Money supports at most two decimal places.");
        }

        Amount = amount;
    }

    public decimal Amount { get; }

    public static Money Positive(decimal amount)
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount must be positive.");
        }

        return new Money(amount);
    }
}
