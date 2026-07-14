namespace RealtimePix.ApiGateway.Domain;

public enum AccountBankRoute
{
    BankA,
    BankB
}

public static class AccountBankRouting
{
    public static AccountBankRoute Resolve(string accountId, string? bankId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        if (string.Equals(bankId, "bank-a", StringComparison.OrdinalIgnoreCase))
        {
            return AccountBankRoute.BankA;
        }

        if (string.Equals(bankId, "bank-b", StringComparison.OrdinalIgnoreCase))
        {
            return AccountBankRoute.BankB;
        }

        if (!string.IsNullOrWhiteSpace(bankId))
        {
            throw new ArgumentOutOfRangeException(nameof(bankId), bankId, "Unknown bank identifier.");
        }

        return accountId.EndsWith("_bank-b", StringComparison.OrdinalIgnoreCase)
            ? AccountBankRoute.BankB
            : AccountBankRoute.BankA;
    }
}
