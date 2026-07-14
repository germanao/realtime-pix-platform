using Microsoft.EntityFrameworkCore;
using RealtimePix.BankLedger.Application;

namespace RealtimePix.BankLedger.Infrastructure;

public sealed class EfTransactionalExecutor(BankLedgerDbContext dbContext) : ITransactionalExecutor
{
    public async Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
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

public sealed class NoopTransactionalExecutor : ITransactionalExecutor
{
    public Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken) =>
        operation(cancellationToken);
}
