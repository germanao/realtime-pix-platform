using Azure.Monitor.OpenTelemetry.AspNetCore;
using RealtimePix.ApiGateway.Api;
using RealtimePix.ApiGateway.Application;
using RealtimePix.ApiGateway.Infrastructure;
using RealtimePix.Eventing;

const string ServiceName = "api-gateway";

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddRealtimePixAzureAppConfiguration();
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
        context.ProblemDetails.Extensions["service"] = ServiceName;
    };
});
builder.Services.AddOpenApi();
builder.Services.AddCors();
builder.Services.AddGatewayInfrastructure();
if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    builder.Services.AddOpenTelemetry().UseAzureMonitor();
}

var app = builder.Build();
app.UseExceptionHandler();
app.UseCors(policy => policy
    .SetIsOriginAllowed(origin => CorsOrigins.IsAllowed(origin, app.Configuration))
    .AllowAnyHeader()
    .AllowAnyMethod());
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/health", () => Results.Ok(new { service = ServiceName, status = "ok" }));
app.MapGet("/health/live", () => Results.Ok(new { service = ServiceName, status = "live" }));
app.MapGet("/health/ready", async (
    IBackendClient backend,
    CancellationToken cancellationToken) =>
{
    var dependencies = new[]
    {
        BackendServices.IdentityPresence,
        BackendServices.BankA,
        BackendServices.BankB,
        BackendServices.Transaction,
        BackendServices.RealtimeEvents
    };
    var checks = await Task.WhenAll(dependencies.Select(async service =>
    {
        var response = await backend.SendAsync(
            new BackendRequest(service, "/health/ready", HttpMethod.Get),
            cancellationToken);
        return new { service, ready = response.IsSuccess };
    }));
    return checks.All(item => item.ready)
        ? Results.Ok(new { service = ServiceName, status = "ready", dependencies = checks })
        : Results.Json(
            new { service = ServiceName, status = "not-ready", dependencies = checks },
            statusCode: StatusCodes.Status503ServiceUnavailable);
});

MapProxyPost("/sessions/anonymous", BackendServices.IdentityPresence, "/sessions/anonymous");
MapProxyGet("/presence/users", BackendServices.IdentityPresence, "/presence/users");
MapProxyPost("/presence/heartbeat", BackendServices.IdentityPresence, "/presence/heartbeat");
MapProxyPost("/presence/leave", BackendServices.IdentityPresence, "/presence/leave");

app.MapPost("/wallet/users/{userId}/bootstrap", async (
    string userId,
    WalletGatewayHandler handler,
    CancellationToken cancellationToken) =>
    ToResult(await handler.BootstrapAsync(userId, cancellationToken)));

app.MapGet("/wallet/accounts", async (
    HttpContext context,
    WalletGatewayHandler handler,
    CancellationToken cancellationToken) =>
    ToResult(await handler.GetAccountsAsync(context.Request.QueryString.Value, cancellationToken)));

app.MapPost("/wallet/accounts/{accountId}/deposit", async (
    string accountId,
    string? bankId,
    HttpContext context,
    WalletGatewayHandler handler,
    CancellationToken cancellationToken) =>
{
    var body = await ReadBodyAsync(context.Request, cancellationToken);
    return ToResult(await handler.DepositAsync(
        accountId,
        bankId,
        body,
        context.Request.ContentType,
        cancellationToken));
});

app.MapGet("/wallet/accounts/{accountId}/transactions", async (
    string accountId,
    string? bankId,
    HttpContext context,
    WalletGatewayHandler handler,
    CancellationToken cancellationToken) =>
    ToResult(await handler.GetTransactionsAsync(
        accountId,
        bankId,
        context.Request.QueryString.Value,
        cancellationToken)));

MapProxyPost("/pix/transfers", BackendServices.Transaction, "/pix/transfers");
MapProxyGet("/pix/transfers/{transferId}", BackendServices.Transaction, "/pix/transfers/{transferId}");
MapProxyGet("/pix/transfers/{transferId}/transitions", BackendServices.Transaction, "/pix/transfers/{transferId}/transitions");
MapProxyGet("/realtime/token", BackendServices.RealtimeEvents, "/realtime/token");
MapProxyGet("/events/timeline", BackendServices.RealtimeEvents, "/events/timeline");
MapProxyGet("/events/transfers/{transferId}/flow", BackendServices.RealtimeEvents, "/events/transfers/{transferId}/flow");

app.Run();

void MapProxyGet(string route, string service, string targetTemplate)
{
    app.MapGet(route, async (
        HttpContext context,
        ProxyRequestHandler handler,
        CancellationToken cancellationToken) =>
    {
        var target = ExpandRoute(targetTemplate, context.Request.RouteValues);
        return ToResult(await handler.HandleAsync(
            new BackendRequest(service, target, HttpMethod.Get, context.Request.QueryString.Value),
            cancellationToken));
    });
}

void MapProxyPost(string route, string service, string targetTemplate)
{
    app.MapPost(route, async (
        HttpContext context,
        ProxyRequestHandler handler,
        CancellationToken cancellationToken) =>
    {
        var body = await ReadBodyAsync(context.Request, cancellationToken);
        var target = ExpandRoute(targetTemplate, context.Request.RouteValues);
        return ToResult(await handler.HandleAsync(
            new BackendRequest(
                service,
                target,
                HttpMethod.Post,
                context.Request.QueryString.Value,
                body,
                context.Request.ContentType),
            cancellationToken));
    });
}

static string ExpandRoute(string template, RouteValueDictionary routeValues)
{
    foreach (var (key, value) in routeValues)
    {
        template = template.Replace(
            $"{{{key}}}",
            Uri.EscapeDataString(Convert.ToString(value) ?? string.Empty),
            StringComparison.Ordinal);
    }

    return template;
}

static async Task<string?> ReadBodyAsync(HttpRequest request, CancellationToken cancellationToken)
{
    if (request.ContentLength is not > 0)
    {
        return null;
    }

    using var reader = new StreamReader(request.Body);
    return await reader.ReadToEndAsync(cancellationToken);
}

static IResult ToResult(BackendResponse response) =>
    Results.Content(response.Content, response.ContentType, statusCode: response.StatusCode);

public partial class Program;
