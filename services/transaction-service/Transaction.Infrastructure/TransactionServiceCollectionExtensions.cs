using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RealtimePix.Eventing;
using RealtimePix.Persistence;
using RealtimePix.Transaction.Application;

namespace RealtimePix.Transaction.Infrastructure;

public static class TransactionServiceCollectionExtensions
{
    public static IServiceCollection AddTransferSaga(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var sagaOptions = new SagaOptions
        {
            StepTimeout = TimeSpan.FromSeconds(configuration.GetValue("Saga:StepTimeoutSeconds", 30)),
            SimulatedCreditTimeout = TimeSpan.FromSeconds(configuration.GetValue("Saga:SimulatedCreditTimeoutSeconds", 8)),
            CompensationTimeout = TimeSpan.FromSeconds(configuration.GetValue("Saga:CompensationTimeoutSeconds", 30)),
            TimeoutBatchSize = configuration.GetValue("Saga:TimeoutBatchSize", 50)
        };
        ValidateOptions(sagaOptions);
        services.AddSingleton(sagaOptions);
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<ISagaSimulationPolicy>(new ConfigurableSagaSimulationPolicy(
            configuration.GetValue("Saga:AllowFailureSimulation", false)));

        services.AddScoped<CreateTransferHandler>();
        services.AddScoped<GetTransferHandler>();
        services.AddScoped<GetSagaTransitionsHandler>();
        services.AddScoped<ProcessSagaOutcomeHandler>();
        services.AddScoped<ProcessExpiredSagasHandler>();
        services.AddScoped<ISagaMessagePublisher, SagaMessagePublisher>();
        services.AddScoped<IIntegrationEventHandler, SagaOutcomeIntegrationHandler>();
        services.AddHostedService<SagaTimeoutWorker>();

        var connectionString = configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddSingleton<ITransferSagaRepository, InMemoryTransferSagaRepository>();
            services.AddSingleton<ITransactionBoundary, NoopTransactionBoundary>();
            services.AddSingleton<ITransactionReadinessProbe, LocalTransactionReadinessProbe>();
        }
        else
        {
            services.AddRealtimePixPostgres<TransactionSagaDbContext>(configuration);
            services.AddScoped<ITransferSagaRepository, EfTransferSagaRepository>();
            services.AddScoped<ITransactionBoundary, EfTransactionBoundary>();
            services.AddScoped<ITransactionReadinessProbe, EfTransactionReadinessProbe>();
            services.AddRealtimePixEfCoreEventing<TransactionSagaDbContext>();
        }

        return services;
    }

    private static void ValidateOptions(SagaOptions options)
    {
        if (options.StepTimeout <= TimeSpan.Zero ||
            options.SimulatedCreditTimeout <= TimeSpan.Zero ||
            options.CompensationTimeout <= TimeSpan.Zero ||
            options.TimeoutBatchSize <= 0)
        {
            throw new InvalidOperationException("Saga timeout configuration values must be positive.");
        }
    }
}
