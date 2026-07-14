using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RealtimePix.BankLedger.Application;
using RealtimePix.Contracts;
using RealtimePix.Eventing;
using RealtimePix.Persistence;

namespace RealtimePix.BankLedger.Infrastructure;

public static class BankLedgerServiceCollectionExtensions
{
    public static IServiceCollection AddBankLedger(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var bankId = configuration["Bank:Id"] ?? BankIds.BankA;
        if (!BankIds.All.Contains(bankId, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Bank:Id '{bankId}' is not supported.");
        }

        var bankName = configuration["Bank:Name"] ?? (bankId == BankIds.BankA ? "Bank A" : "Bank B");
        var welcomeBalance = configuration.GetValue<decimal?>("Bank:WelcomeBalance")
            ?? (bankId == BankIds.BankA ? 10_000m : 0m);
        var descriptor = new BankDescriptor(bankId, bankName, welcomeBalance);
        services.AddSingleton(descriptor);

        services.AddScoped<BootstrapAccountHandler>();
        services.AddScoped<GetAccountsHandler>();
        services.AddScoped<DepositFundsHandler>();
        services.AddScoped<GetLedgerEntriesHandler>();
        services.AddScoped<ProcessBankCommandHandler>();
        services.AddScoped<IBankEventPublisher, BankEventPublisher>();
        services.AddScoped<IIntegrationEventHandler, BankCommandIntegrationHandler>();

        var connectionString = configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddSingleton<IBankLedgerRepository, InMemoryBankLedgerRepository>();
            services.AddSingleton<ITransactionalExecutor, NoopTransactionalExecutor>();
            services.AddSingleton<IBankReadinessProbe, LocalBankReadinessProbe>();
        }
        else
        {
            services.AddRealtimePixPostgres<BankLedgerDbContext>(configuration);
            services.AddScoped<IBankLedgerRepository, EfBankLedgerRepository>();
            services.AddScoped<ITransactionalExecutor, EfTransactionalExecutor>();
            services.AddScoped<IBankReadinessProbe, EfBankReadinessProbe>();
            services.AddRealtimePixEfCoreEventing<BankLedgerDbContext>();
        }

        return services;
    }
}
