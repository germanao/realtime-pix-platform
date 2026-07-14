using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RealtimePix.Transaction.Application;

namespace RealtimePix.Transaction.Infrastructure;

public sealed class SagaTimeoutWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<SagaTimeoutWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var handler = scope.ServiceProvider.GetRequiredService<ProcessExpiredSagasHandler>();
                var processed = await handler.HandleAsync(stoppingToken);
                if (processed > 0)
                {
                    logger.LogInformation("Processed {ExpiredSagaCount} expired transfer Sagas.", processed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Checking transfer Saga deadlines failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }
}
