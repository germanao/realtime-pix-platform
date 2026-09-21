using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace RealtimePix.Persistence;

public static class PostgresServiceCollectionExtensions
{
    public static IServiceCollection AddRealtimePixPostgres<TContext>(
        this IServiceCollection services,
        IConfiguration configuration)
        where TContext : DbContext
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is required for PostgreSQL mode.");

        services.AddDbContext<TContext>(options => options.UseNpgsql(connectionString));
        return services;
    }
}
