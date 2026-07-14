using Microsoft.EntityFrameworkCore;
using RealtimeEvents.Application;

namespace RealtimeEvents.Infrastructure;

public sealed class EfRealtimeProjectionTransaction(RealtimeProjectionDbContext dbContext) : IRealtimeProjectionTransaction
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

public sealed class NoopRealtimeProjectionTransaction : IRealtimeProjectionTransaction
{
    public Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken) =>
        operation(cancellationToken);
}
