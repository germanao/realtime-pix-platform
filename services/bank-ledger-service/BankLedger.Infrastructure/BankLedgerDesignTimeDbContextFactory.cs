using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RealtimePix.BankLedger.Infrastructure;

public sealed class BankLedgerDesignTimeDbContextFactory : IDesignTimeDbContextFactory<BankLedgerDbContext>
{
    public BankLedgerDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("BANK_LEDGER_MIGRATIONS_CONNECTION")
            ?? throw new InvalidOperationException(
                "Set BANK_LEDGER_MIGRATIONS_CONNECTION before using EF Core design-time tooling.");
        var options = new DbContextOptionsBuilder<BankLedgerDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new BankLedgerDbContext(options);
    }
}
