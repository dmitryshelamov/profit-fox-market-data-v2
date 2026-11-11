using FluentMigrator.Runner;

using Microsoft.Extensions.DependencyInjection;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres.FluentMigrator.Extensions;

public static class FluentMigratorBuilderExtensions
{
    /// <summary>Applies every migration the database does not have yet.</summary>
    public static void MigrateToLatestVersion(this IServiceProvider services)
    {
        using IServiceScope scope = services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp();
    }
}