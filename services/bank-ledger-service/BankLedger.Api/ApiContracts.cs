using RealtimePix.BankLedger.Application;

namespace RealtimePix.BankLedger.Api;

public sealed record DepositRequest(string UserId, decimal Amount, string? Reason);

public sealed record BankBootstrapResponse(
    AccountDto PrimaryAccount,
    bool AccountCreated,
    bool WelcomeCreditApplied);
