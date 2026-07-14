using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Diagnostics;
using RealtimePix.Eventing;
using RealtimePix.Transaction.Application;
using RealtimePix.Transaction.Infrastructure;

const string ServiceName = "transaction-service";

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
if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    builder.Services.AddOpenTelemetry().UseAzureMonitor();
}

builder.Services.AddRealtimePixEventBus(builder.Configuration, ServiceName);
builder.Services.AddTransferSaga(builder.Configuration);

var app = builder.Build();
app.UseExceptionHandler(exceptionHandlerApp =>
{
    exceptionHandlerApp.Run(async context =>
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        var (statusCode, title, type, detail) = exception switch
        {
            ArgumentException argument => (
                StatusCodes.Status400BadRequest,
                "Invalid transfer request",
                "https://realtime-pix.dev/problems/validation",
                argument.Message),
            SagaSimulationNotAllowedException simulation => (
                StatusCodes.Status400BadRequest,
                "Simulation is disabled",
                "https://realtime-pix.dev/problems/simulation-disabled",
                simulation.Message),
            SagaConcurrencyException => (
                StatusCodes.Status409Conflict,
                "Concurrent Saga update",
                "https://realtime-pix.dev/problems/saga-concurrency",
                "The transfer changed concurrently. Retry the request."),
            _ => (
                StatusCodes.Status500InternalServerError,
                "Unexpected service error",
                "https://realtime-pix.dev/problems/internal",
                (string?)null)
        };
        await Results.Problem(statusCode: statusCode, title: title, detail: detail, type: type)
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

app.MapGet("/health", () => Results.Ok(new { service = ServiceName, status = "ok" }));
app.MapGet("/health/live", () => Results.Ok(new { service = ServiceName, status = "live" }));
app.MapGet("/health/ready", async (ITransactionReadinessProbe probe, CancellationToken cancellationToken) =>
{
    var result = await probe.CheckAsync(cancellationToken);
    return result.IsReady
        ? Results.Ok(new { service = ServiceName, status = "ready" })
        : Results.Json(
            new { service = ServiceName, status = "not-ready", reason = result.Reason },
            statusCode: StatusCodes.Status503ServiceUnavailable);
});

app.MapPost("/pix/transfers", async (
    PixTransferRequest request,
    CreateTransferHandler handler,
    CancellationToken cancellationToken) =>
{
    var result = await handler.HandleAsync(request, cancellationToken);
    return Results.Accepted($"/pix/transfers/{result.Transfer.TransferId}", result.Transfer);
});

app.MapGet("/pix/transfers/{transferId}", async (
    string transferId,
    GetTransferHandler handler,
    CancellationToken cancellationToken) =>
{
    var transfer = await handler.HandleAsync(transferId, cancellationToken);
    return transfer is null
        ? Results.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Transfer not found",
            type: "https://realtime-pix.dev/problems/transfer-not-found")
        : Results.Ok(transfer);
});

app.MapGet("/pix/transfers/{transferId}/transitions", async (
    string transferId,
    GetSagaTransitionsHandler handler,
    CancellationToken cancellationToken) =>
{
    var transitions = await handler.HandleAsync(transferId, cancellationToken);
    return Results.Ok(transitions);
});

app.Run();

public partial class Program;
