using FluentMigrator.Runner;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Infrastructure.Exchanges;
using ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres;

internal static class PostgresRegistrar
{
    /// <summary>The connection string is read on first use, so a test host can still replace it after registration.</summary>
    public static void Configure(IConfiguration configuration, IServiceCollection services)
    {
        services.AddSingleton(_ => NpgsqlDataSource.Create(ConnectionString(configuration)));
        services.AddSingleton<IMarketPairRepository, MarketPairRepository>();
        services.AddSingleton<IMarketStore, MarketStore>();
        services.AddSingleton<IRestPauseStore, CollectorStateRepository>();
        services
            .AddFluentMigratorCore()
            .ConfigureRunner(runner => runner
                .AddPostgres()
                .WithGlobalConnectionString(_ => ConnectionString(configuration))
                .ScanIn(typeof(PostgresRegistrar).Assembly)
                .For.Migrations());
    }

    private static string ConnectionString(IConfiguration configuration)
    {
        return configuration.GetConnectionString("MarketStore")!;
    }
}