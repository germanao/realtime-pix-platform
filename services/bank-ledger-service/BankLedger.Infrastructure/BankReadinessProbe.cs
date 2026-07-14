using Microsoft.Extensions.Logging;
using RealtimePix.BankLedger.Application;
using RealtimePix.Eventing;

namespace RealtimePix.BankLedger.Infrastructure;

public sealed class EfBankReadinessProbe(
    BankLedgerDbContext dbContext,
    IEventBusReadinessProbe eventBus,
    ILogger<EfBankReadinessProbe> logger) : IBankReadinessProbe
{
    public async Task<ReadinessResult> CheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);
            if (!canConnect)
            {
                return new ReadinessResult(false, "PostgreSQL did not accept a connection.");
            }

            var broker = await eventBus.CheckAsync(cancellationToken);
            return broker.IsReady
                ? new ReadinessResult(true)
                : new ReadinessResult(false, "event-bus-unavailable");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The bank ledger PostgreSQL readiness probe failed.");
            return new ReadinessResult(false, "database-unavailable");
        }
    }
}

public sealed class LocalBankReadinessProbe(IEventBusReadinessProbe eventBus) : IBankReadinessProbe
{
    public async Task<ReadinessResult> CheckAsync(CancellationToken cancellationToken)
    {
        var broker = await eventBus.CheckAsync(cancellationToken);
        return broker.IsReady
            ? new ReadinessResult(true)
            : new ReadinessResult(false, "event-bus-unavailable");
    }
}
