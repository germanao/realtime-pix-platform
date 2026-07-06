using System.Text;

const string ServiceName = "api-gateway";

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCors();

var app = builder.Build();
app.UseCors(policy => policy.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin());

app.MapGet("/health", () => Results.Ok(new { service = ServiceName, status = "ok" }));
app.MapGet("/health/live", () => Results.Ok(new { service = ServiceName, status = "live" }));
app.MapGet("/health/ready", () => Results.Ok(new { service = ServiceName, status = "ready" }));

app.MapPost("/sessions/anonymous", (HttpContext context, IConfiguration configuration, CancellationToken cancellationToken) =>
    GatewayProxy.ForwardAsync(context, configuration, "IdentityPresence", "/sessions/anonymous", HttpMethod.Post, cancellationToken));

app.MapGet("/presence/users", (HttpContext context, IConfiguration configuration, CancellationToken cancellationToken) =>
    GatewayProxy.ForwardAsync(context, configuration, "IdentityPresence", "/presence/users", HttpMethod.Get, cancellationToken));

app.MapPost("/presence/heartbeat", (HttpContext context, IConfiguration configuration, CancellationToken cancellationToken) =>
    GatewayProxy.ForwardAsync(context, configuration, "IdentityPresence", "/presence/heartbeat", HttpMethod.Post, cancellationToken));

app.MapPost("/presence/leave", (HttpContext context, IConfiguration configuration, CancellationToken cancellationToken) =>
    GatewayProxy.ForwardAsync(context, configuration, "IdentityPresence", "/presence/leave", HttpMethod.Post, cancellationToken));

app.MapGet("/wallet/accounts", (HttpContext context, IConfiguration configuration, CancellationToken cancellationToken) =>
    GatewayProxy.ForwardAsync(context, configuration, "WalletLedger", "/wallet/accounts", HttpMethod.Get, cancellationToken));

app.MapPost("/wallet/users/{userId}/bootstrap", (string userId, HttpContext context, IConfiguration configuration, CancellationToken cancellationToken) =>
    GatewayProxy.ForwardAsync(context, configuration, "WalletLedger", $"/wallet/users/{Uri.EscapeDataString(userId)}/bootstrap", HttpMethod.Post, cancellationToken));

app.MapPost("/wallet/accounts/{accountId}/deposit", (string accountId, HttpContext context, IConfiguration configuration, CancellationToken cancellationToken) =>
    GatewayProxy.ForwardAsync(context, configuration, "WalletLedger", $"/wallet/accounts/{Uri.EscapeDataString(accountId)}/deposit", HttpMethod.Post, cancellationToken));

app.MapGet("/wallet/accounts/{accountId}/transactions", (string accountId, HttpContext context, IConfiguration configuration, CancellationToken cancellationToken) =>
    GatewayProxy.ForwardAsync(context, configuration, "WalletLedger", $"/wallet/accounts/{Uri.EscapeDataString(accountId)}/transactions", HttpMethod.Get, cancellationToken));

app.MapPost("/pix/transfers", (HttpContext context, IConfiguration configuration, CancellationToken cancellationToken) =>
    GatewayProxy.ForwardAsync(context, configuration, "Transaction", "/pix/transfers", HttpMethod.Post, cancellationToken));

app.MapGet("/pix/transfers/{transferId}", (string transferId, HttpContext context, IConfiguration configuration, CancellationToken cancellationToken) =>
    GatewayProxy.ForwardAsync(context, configuration, "Transaction", $"/pix/transfers/{Uri.EscapeDataString(transferId)}", HttpMethod.Get, cancellationToken));

app.MapGet("/realtime/token", (HttpContext context, IConfiguration configuration, CancellationToken cancellationToken) =>
    GatewayProxy.ForwardAsync(context, configuration, "RealtimeEvents", "/realtime/token", HttpMethod.Get, cancellationToken));

app.MapGet("/events/timeline", (HttpContext context, IConfiguration configuration, CancellationToken cancellationToken) =>
    GatewayProxy.ForwardAsync(context, configuration, "RealtimeEvents", "/events/timeline", HttpMethod.Get, cancellationToken));

app.MapGet("/events/transfers/{transferId}/flow", (string transferId, HttpContext context, IConfiguration configuration, CancellationToken cancellationToken) =>
    GatewayProxy.ForwardAsync(context, configuration, "RealtimeEvents", $"/events/transfers/{Uri.EscapeDataString(transferId)}/flow", HttpMethod.Get, cancellationToken));

app.Run();

public static class GatewayProxy
{
    private static readonly HttpClient Client = new();

    public static async Task<IResult> ForwardAsync(
        HttpContext context,
        IConfiguration configuration,
        string serviceName,
        string path,
        HttpMethod method,
        CancellationToken cancellationToken)
    {
        var baseUrl = configuration[$"Services:{serviceName}"];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return Results.Problem($"Service URL for {serviceName} is not configured.");
        }

        var target = BuildTargetUri(baseUrl, path, context.Request.QueryString.Value);
        using var message = new HttpRequestMessage(method, target);

        if (method != HttpMethod.Get && context.Request.ContentLength is > 0)
        {
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
            var body = await reader.ReadToEndAsync(cancellationToken);
            message.Content = new StringContent(body, Encoding.UTF8, context.Request.ContentType ?? "application/json");
        }

        using var response = await Client.SendAsync(message, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/json";

        return Results.Content(content, contentType, statusCode: (int)response.StatusCode);
    }

    private static string BuildTargetUri(string baseUrl, string path, string? queryString)
    {
        return $"{baseUrl.TrimEnd('/')}{path}{queryString}";
    }
}
