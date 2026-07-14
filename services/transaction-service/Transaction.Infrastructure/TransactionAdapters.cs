using Microsoft.Extensions.Logging;
using RealtimePix.Eventing;
using RealtimePix.Transaction.Application;
using RealtimePix.Transaction.Domain;

namespace RealtimePix.Transaction.Infrastructure;

public sealed class EfTransactionBoundary(TransactionSagaDbContext dbContext) : ITransactionBoundary
{
    public async Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        if (dbContext.Database.CurrentTransaction is not null)
        {
            await operation(cancellationToken);
            return;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await operation(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            throw;
        }
    }
}

public sealed class NoopTransactionBoundary : ITransactionBoundary
{
    public Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken) =>
        operation(cancellationToken);
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public sealed class ConfigurableSagaSimulationPolicy(bool allowFailureSimulation) : ISagaSimulationPolicy
{
    public void EnsureAllowed(TransferSimulationMode mode)
    {
        if (!allowFailureSimulation && mode != TransferSimulationMode.Normal)
        {
            throw new SagaSimulationNotAllowedException();
        }
    }
}

public sealed class EfTransactionReadinessProbe(
    TransactionSagaDbContext dbContext,
    IEventBusReadinessProbe eventBus,
    ILogger<EfTransactionReadinessProbe> logger) : ITransactionReadinessProbe
{
    public async Task<ReadinessResult> CheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!await dbContext.Database.CanConnectAsync(cancellationToken))
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
            logger.LogWarning(ex, "The transaction PostgreSQL readiness probe failed.");
            return new ReadinessResult(false, "database-unavailable");
        }
    }
}

public sealed class LocalTransactionReadinessProbe(IEventBusReadinessProbe eventBus) : ITransactionReadinessProbe
{
    public async Task<ReadinessResult> CheckAsync(CancellationToken cancellationToken)
    {
        var broker = await eventBus.CheckAsync(cancellationToken);
        return broker.IsReady
            ? new ReadinessResult(true)
            : new ReadinessResult(false, "event-bus-unavailable");
    }
}
