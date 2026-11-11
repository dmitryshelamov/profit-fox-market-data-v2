using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using ProfitFox.MarketData.Infrastructure.Persistence.Postgres;
using ProfitFox.MarketData.Infrastructure.Persistence.Postgres.FluentMigrator.Extensions;

using Testcontainers.PostgreSql;

namespace ProfitFox.MarketData.Tests.Integration;

/// <summary>
///     Starts one TimescaleDB container for the whole test assembly with one migrated test database, which
///     <see cref="ResetDatabaseAttribute" /> empties before every test.
/// </summary>
[SetUpFixture]
public class IntegrationEnvironment
{
    // Keep in sync with docker-compose.yml.
    public const string TimescaleImage = "timescale/timescaledb:2.30.2-pg17";

    // FluentMigrator's table of applied migrations: a reset keeps it, so the database stays migrated.
    private const string VersionTable = "VersionInfo";

    private static PostgreSqlContainer Container = null!;

    private static string ResetSql = null!;

    /// <summary>The connection string, with its password, of the test database: migrated, and emptied before every test.</summary>
    public static string ConnectionString { get; private set; } = null!;

    [OneTimeSetUp]
    public async Task StartContainer()
    {
        // Idle pooled connections of finished tests stay open for a while.
        Container = new PostgreSqlBuilder(TimescaleImage).WithCommand("-c", "max_connections=1000").Build();
        await Container.StartAsync();

        ConnectionString = await CreateEmptyDatabaseAsync();
        Migrate(ConnectionString);
        ResetSql = await ReadResetSqlAsync();
    }

    [OneTimeTearDown]
    public async Task StopContainer()
    {
        await Container.DisposeAsync();
    }

    /// <summary>A database of its own without migrations, for a test of the migrations themselves.</summary>
    public static async Task<string> CreateEmptyDatabaseAsync()
    {
        var name = $"test_{Guid.NewGuid():N}";

        await using (var connection = new NpgsqlConnection(Container.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE {name}", connection);
            await command.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(Container.GetConnectionString()) { Database = name }.ConnectionString;
    }

    /// <summary>Brings the test database back to its state right after the migrations.</summary>
    public static void ResetDatabase()
    {
        using var connection = new NpgsqlConnection(ConnectionString);
        connection.Open();
        using var command = new NpgsqlCommand(ResetSql, connection);
        command.ExecuteNonQuery();
    }

    private static void Migrate(string connectionString)
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:MarketStore"] = connectionString })
            .Build();
        var services = new ServiceCollection();
        PostgresRegistrar.Configure(configuration, services);

        using ServiceProvider provider = services.BuildServiceProvider();
        provider.MigrateToLatestVersion();
    }

    /// <summary>
    ///     Truncates every table of the migrated database but the version table, then writes back the rows the migrations
    ///     wrote themselves, such as the single row of the state of the collector.
    /// </summary>
    private static async Task<string> ReadResetSqlAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        var tables = new List<string>();

        await using (var command = new NpgsqlCommand(
                         "SELECT quote_ident(tablename) FROM pg_tables WHERE schemaname = 'public' AND tablename <> @version ORDER BY tablename",
                         connection))
        {
            command.Parameters.AddWithValue("version", VersionTable);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                tables.Add(reader.GetString(0));
            }
        }

        var sql = new List<string> { $"TRUNCATE TABLE {string.Join(", ", tables)} RESTART IDENTITY CASCADE;" };

        foreach (string table in tables)
        {
            await using var command = new NpgsqlCommand(
                $"SELECT format('INSERT INTO %s SELECT * FROM json_populate_recordset(NULL::%s, %L);', '{table}', '{table}', json_agg(t)::text) FROM {table} t HAVING count(*) > 0",
                connection);

            if (await command.ExecuteScalarAsync() is string insert)
            {
                sql.Add(insert);
            }
        }

        return string.Join("\n", sql);
    }
}