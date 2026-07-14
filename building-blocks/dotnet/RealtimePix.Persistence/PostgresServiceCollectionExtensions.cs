using Azure.Core;
using Azure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace RealtimePix.Persistence;

public static class PostgresServiceCollectionExtensions
{
    private static readonly string[] AzurePostgresScopes =
        ["https://ossrdbms-aad.database.windows.net/.default"];

    public static IServiceCollection AddRealtimePixPostgres<TContext>(
        this IServiceCollection services,
        IConfiguration configuration)
        where TContext : DbContext
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is required for PostgreSQL mode.");

        var managedIdentityEnabled = bool.TryParse(
            configuration["Postgres:UseManagedIdentity"],
            out var useManagedIdentity) && useManagedIdentity;
        if (!managedIdentityEnabled)
        {
            services.AddDbContext<TContext>(options => options.UseNpgsql(connectionString));
            return services;
        }

        var connection = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Password = null
        };
        if (string.IsNullOrWhiteSpace(connection.Username))
        {
            connection.Username = configuration["Postgres:PrincipalName"]
                ?? throw new InvalidOperationException("Postgres:PrincipalName is required for managed identity authentication.");
        }

        var clientId = configuration["AZURE_CLIENT_ID"];
        var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            ManagedIdentityClientId = string.IsNullOrWhiteSpace(clientId) ? null : clientId
        });
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connection.ConnectionString);
        dataSourceBuilder.UsePeriodicPasswordProvider(
            async (_, cancellationToken) =>
            {
                var token = await credential.GetTokenAsync(
                    new TokenRequestContext(AzurePostgresScopes),
                    cancellationToken);
                return token.Token;
            },
            TimeSpan.FromMinutes(50),
            TimeSpan.FromSeconds(10));

        services.AddSingleton(dataSourceBuilder.Build());
        services.AddDbContext<TContext>((serviceProvider, options) =>
            options.UseNpgsql(serviceProvider.GetRequiredService<NpgsqlDataSource>()));
        return services;
    }
}
