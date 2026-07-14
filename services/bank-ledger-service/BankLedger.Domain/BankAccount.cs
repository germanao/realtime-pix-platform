namespace RealtimePix.BankLedger.Domain;

public sealed class BankAccount
{
    public BankAccount(string accountId, string userId, string bankId, string bankName, decimal balance)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(bankId);
        ArgumentException.ThrowIfNullOrWhiteSpace(bankName);
        if (balance < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(balance), "Account balance cannot be negative.");
        }

        AccountId = accountId;
        UserId = userId;
        BankId = bankId;
        BankName = bankName;
        Balance = balance;
    }

    public string AccountId { get; }

    public string UserId { get; }

    public string BankId { get; }

    public string BankName { get; }

    public decimal Balance { get; private set; }

    public void Credit(Money amount) => Balance += amount.Amount;
}
