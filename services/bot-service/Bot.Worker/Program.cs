using Azure.Monitor.OpenTelemetry.AspNetCore;
using Bot.Application;
using Bot.Infrastructure;
using RealtimePix.Eventing;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddRealtimePixAzureAppConfiguration();
if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    builder.Services.AddOpenTelemetry().UseAzureMonitor();
}
builder.Services.AddRealtimePixEventBus(builder.Configuration, BotMetadata.ServiceName);
builder.Services.AddBotInfrastructure(builder.Configuration);
builder.Services.AddHostedService<BotMaintenanceWorker>();

var app = builder.Build();
app.MapGet("/health", () => Results.Ok(new { service = BotMetadata.ServiceName, status = "ok" }));
app.MapGet("/health/live", () => Results.Ok(new { service = BotMetadata.ServiceName, status = "live" }));
app.MapGet("/health/ready", async (IBotReadinessProbe probe, CancellationToken cancellationToken) =>
{
    var result = await probe.CheckAsync(cancellationToken);
    var body = new
    {
        service = BotMetadata.ServiceName,
        status = result.IsReady ? "ready" : "not-ready",
        dependencies = new { gateway = result.GatewayReady, eventBus = result.EventBusReady },
        reason = result.Reason
    };
    return result.IsReady
        ? Results.Ok(body)
        : Results.Json(body, statusCode: StatusCodes.Status503ServiceUnavailable);
});
app.Run();

public sealed class BotMaintenanceWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<BotMaintenanceWorker> logger) : BackgroundService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = TimeSpan.Zero;
        while (!stoppingToken.IsCancellationRequested)
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, stoppingToken);
            }

            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<MaintainBotsHandler>().ExecuteAsync(stoppingToken);
                delay = RefreshInterval;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Bot maintenance will retry after a dependency failure.");
                delay = RetryInterval;
            }
        }
    }
}

public partial class Program;
