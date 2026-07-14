using System.Text.Json;
using RealtimePix.ApiGateway.Domain;

namespace RealtimePix.ApiGateway.Application;

public sealed class ProxyRequestHandler(IBackendClient client)
{
    public Task<BackendResponse> HandleAsync(BackendRequest request, CancellationToken cancellationToken) =>
        client.SendAsync(request, cancellationToken);
}

public sealed class WalletGatewayHandler(IBackendClient client)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<BackendResponse> BootstrapAsync(string userId, CancellationToken cancellationToken)
    {
        var escapedUserId = Uri.EscapeDataString(userId);
        var bankATask = client.SendAsync(
            new BackendRequest(BackendServices.BankA, $"/wallet/users/{escapedUserId}/bootstrap", HttpMethod.Post),
            cancellationToken);
        var bankBTask = client.SendAsync(
            new BackendRequest(BackendServices.BankB, $"/wallet/users/{escapedUserId}/bootstrap", HttpMethod.Post),
            cancellationToken);
        await Task.WhenAll(bankATask, bankBTask);

        var bankAResponse = await bankATask;
        var bankBResponse = await bankBTask;
        if (!bankAResponse.IsSuccess)
        {
            return bankAResponse;
        }

        if (!bankBResponse.IsSuccess)
        {
            return bankBResponse;
        }

        var bankA = JsonSerializer.Deserialize<BankBootstrapResponse>(bankAResponse.Content, JsonOptions)
            ?? throw new InvalidOperationException("Bank A returned an invalid bootstrap response.");
        return Json(
            200,
            new WalletBootstrapResponse(bankA.PrimaryAccount, bankA.WelcomeCreditApplied));
    }

    public async Task<BackendResponse> GetAccountsAsync(
        string? queryString,
        CancellationToken cancellationToken)
    {
        var bankATask = client.SendAsync(
            new BackendRequest(BackendServices.BankA, "/wallet/accounts", HttpMethod.Get, queryString),
            cancellationToken);
        var bankBTask = client.SendAsync(
            new BackendRequest(BackendServices.BankB, "/wallet/accounts", HttpMethod.Get, queryString),
            cancellationToken);
        await Task.WhenAll(bankATask, bankBTask);

        var bankAResponse = await bankATask;
        var bankBResponse = await bankBTask;
        if (!bankAResponse.IsSuccess)
        {
            return bankAResponse;
        }

        if (!bankBResponse.IsSuccess)
        {
            return bankBResponse;
        }

        var accounts = DeserializeAccounts(bankAResponse.Content)
            .Concat(DeserializeAccounts(bankBResponse.Content))
            .OrderBy(item => item.BankId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return Json(200, accounts);
    }

    public Task<BackendResponse> DepositAsync(
        string accountId,
        string? bankId,
        string? body,
        string? contentType,
        CancellationToken cancellationToken) =>
        client.SendAsync(
            new BackendRequest(
                ResolveService(accountId, bankId),
                $"/wallet/accounts/{Uri.EscapeDataString(accountId)}/deposit",
                HttpMethod.Post,
                Body: body,
                ContentType: contentType),
            cancellationToken);

    public Task<BackendResponse> GetTransactionsAsync(
        string accountId,
        string? bankId,
        string? queryString,
        CancellationToken cancellationToken) =>
        client.SendAsync(
            new BackendRequest(
                ResolveService(accountId, bankId),
                $"/wallet/accounts/{Uri.EscapeDataString(accountId)}/transactions",
                HttpMethod.Get,
                queryString),
            cancellationToken);

    private static string ResolveService(string accountId, string? bankId) =>
        AccountBankRouting.Resolve(accountId, bankId) == AccountBankRoute.BankA
            ? BackendServices.BankA
            : BackendServices.BankB;

    private static AccountResponse[] DeserializeAccounts(string content) =>
        JsonSerializer.Deserialize<AccountResponse[]>(content, JsonOptions)
        ?? throw new InvalidOperationException("A bank returned an invalid account response.");

    private static BackendResponse Json<T>(int statusCode, T value) =>
        new(statusCode, JsonSerializer.Serialize(value, JsonOptions), "application/json");
}
