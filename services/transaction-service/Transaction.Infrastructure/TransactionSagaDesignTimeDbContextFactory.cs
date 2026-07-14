using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RealtimePix.Transaction.Infrastructure;

public sealed class TransactionSagaDesignTimeDbContextFactory : IDesignTimeDbContextFactory<TransactionSagaDbContext>
{
    public TransactionSagaDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("TRANSACTION_MIGRATIONS_CONNECTION")
            ?? throw new InvalidOperationException(
                "Set TRANSACTION_MIGRATIONS_CONNECTION before using EF Core design-time tooling.");
        var options = new DbContextOptionsBuilder<TransactionSagaDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new TransactionSagaDbContext(options);
    }
}
