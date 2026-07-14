namespace RealtimePix.ApiGateway.Application;

public static class BackendServices
{
    public const string IdentityPresence = "IdentityPresence";
    public const string BankA = "BankA";
    public const string BankB = "BankB";
    public const string Transaction = "Transaction";
    public const string RealtimeEvents = "RealtimeEvents";
}

public sealed record BackendRequest(
    string ServiceName,
    string Path,
    HttpMethod Method,
    string? QueryString = null,
    string? Body = null,
    string? ContentType = null);

public sealed record BackendResponse(int StatusCode, string Content, string ContentType)
{
    public bool IsSuccess => StatusCode is >= 200 and < 300;
}

public sealed record AccountResponse(
    string AccountId,
    string UserId,
    string BankId,
    string BankName,
    decimal Balance);

public sealed record BankBootstrapResponse(
    AccountResponse PrimaryAccount,
    bool AccountCreated,
    bool WelcomeCreditApplied);

public sealed record WalletBootstrapResponse(
    AccountResponse PrimaryAccount,
    bool WelcomeCreditApplied);

public interface IBackendClient
{
    Task<BackendResponse> SendAsync(BackendRequest request, CancellationToken cancellationToken);
}
