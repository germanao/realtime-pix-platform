using IdentityPresence.Infrastructure;
using Microsoft.EntityFrameworkCore;
using RealtimeEvents.Infrastructure;
using RealtimePix.BankLedger.Infrastructure;
using RealtimePix.Transaction.Infrastructure;
using Xunit;

namespace RealtimePix.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class MigrationTests(PostgreSqlFixture postgres)
{
    [IntegrationFact]
    public async Task All_five_service_databases_migrate_from_empty()
    {
        await AssertMigratesAsync<IdentityPresenceDbContext>("identity", options => new(options));
        await AssertMigratesAsync<BankLedgerDbContext>("bank_a", options => new(options));
        await AssertMigratesAsync<BankLedgerDbContext>("bank_b", options => new(options));
        await AssertMigratesAsync<TransactionSagaDbContext>("transaction", options => new(options));
        await AssertMigratesAsync<RealtimeProjectionDbContext>("realtime", options => new(options));
    }

    private async Task AssertMigratesAsync<TContext>(
        string databasePrefix,
        Func<DbContextOptions<TContext>, TContext> createContext)
        where TContext : DbContext
    {
        var connectionString = await postgres.CreateDatabaseAsync(databasePrefix);
        var options = new DbContextOptionsBuilder<TContext>()
            .UseNpgsql(connectionString)
            .Options;
        await using var context = createContext(options);

        await context.Database.MigrateAsync();

        Assert.NotEmpty(await context.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
    }
}
