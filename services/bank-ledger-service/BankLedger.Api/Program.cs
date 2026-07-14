using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Diagnostics;
using RealtimePix.BankLedger.Api;
using RealtimePix.BankLedger.Application;
using RealtimePix.BankLedger.Infrastructure;
using RealtimePix.Contracts;
using RealtimePix.Eventing;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddRealtimePixAzureAppConfiguration();

var bankId = builder.Configuration["Bank:Id"] ?? BankIds.BankA;
var queueName = BankIds.QueueName(bankId);
var defaults = new Dictionary<string, string?>();
if (string.IsNullOrWhiteSpace(builder.Configuration["EventBus:QueueName"]))
{
    defaults["EventBus:QueueName"] = queueName;
}

if (string.IsNullOrWhiteSpace(builder.Configuration["EventBus:ServiceBus:QueueName"]))
{
    defaults["EventBus:ServiceBus:QueueName"] = queueName;
}

if (defaults.Count > 0)
{
    builder.Configuration.AddInMemoryCollection(defaults);
}

var serviceName = $"{bankId}-ledger-service";
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
        context.ProblemDetails.Extensions["service"] = serviceName;
    };
});
builder.Services.AddOpenApi();
builder.Services.AddCors();
if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    builder.Services.AddOpenTelemetry().UseAzureMonitor();
}

builder.Services.AddRealtimePixEventBus(builder.Configuration, serviceName);
builder.Services.AddBankLedger(builder.Configuration);

var app = builder.Build();
app.UseExceptionHandler(exceptionHandlerApp =>
{
    exceptionHandlerApp.Run(async context =>
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        var statusCode = exception is ArgumentException ? StatusCodes.Status400BadRequest : StatusCodes.Status500InternalServerError;
        await Results.Problem(
            statusCode: statusCode,
            title: statusCode == StatusCodes.Status400BadRequest ? "Invalid request" : "Unexpected service error",
            detail: statusCode == StatusCodes.Status400BadRequest ? exception?.Message : null,
            type: $"https://realtime-pix.dev/problems/{(statusCode == 400 ? "validation" : "internal")}")
            .ExecuteAsync(context);
    });
});
app.UseCors(policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? ["http://localhost:3000"])
    .AllowAnyHeader()
    .AllowAnyMethod());

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/health", () => Results.Ok(new { service = serviceName, bankId, status = "ok" }));
app.MapGet("/health/live", () => Results.Ok(new { service = serviceName, bankId, status = "live" }));
app.MapGet("/health/ready", async (IBankReadinessProbe probe, CancellationToken cancellationToken) =>
{
    var result = await probe.CheckAsync(cancellationToken);
    return result.IsReady
        ? Results.Ok(new { service = serviceName, bankId, status = "ready" })
        : Results.Json(
            new { service = serviceName, bankId, status = "not-ready", reason = result.Reason },
            statusCode: StatusCodes.Status503ServiceUnavailable);
});

app.MapPost("/wallet/users/{userId}/bootstrap", async (
    string userId,
    BootstrapAccountHandler handler,
    CancellationToken cancellationToken) =>
{
    var result = await handler.HandleAsync(userId, cancellationToken);
    return Results.Ok(new BankBootstrapResponse(
        result.Account,
        result.AccountCreated,
        result.WelcomeCreditApplied));
});

app.MapGet("/wallet/accounts", async (
    string userId,
    GetAccountsHandler handler,
    CancellationToken cancellationToken) =>
{
    var accounts = await handler.HandleAsync(userId, cancellationToken);
    return Results.Ok(accounts);
});

app.MapPost("/wallet/accounts/{accountId}/deposit", async (
    string accountId,
    DepositRequest request,
    DepositFundsHandler handler,
    CancellationToken cancellationToken) =>
{
    if (request.Amount <= 0 || decimal.Round(request.Amount, 2) != request.Amount)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Invalid deposit amount",
            detail: "Deposit amount must be positive and use at most two decimal places.",
            type: "https://realtime-pix.dev/problems/invalid-money");
    }

    var result = await handler.HandleAsync(
        accountId,
        request.UserId,
        request.Amount,
        request.Reason ?? "Manual demo deposit",
        cancellationToken);
    return result is null
        ? Results.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Account not found",
            detail: "The account does not belong to this user or bank.",
            type: "https://realtime-pix.dev/problems/account-not-found")
        : Results.Ok(result);
});

app.MapGet("/wallet/accounts/{accountId}/transactions", async (
    string accountId,
    string userId,
    GetLedgerEntriesHandler handler,
    CancellationToken cancellationToken) =>
{
    var entries = await handler.HandleAsync(accountId, userId, cancellationToken);
    return entries is null
        ? Results.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Account not found",
            detail: "The account does not belong to this user or bank.",
            type: "https://realtime-pix.dev/problems/account-not-found")
        : Results.Ok(entries);
});

app.Run();

public partial class Program;
