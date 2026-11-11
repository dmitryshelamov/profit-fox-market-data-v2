using Dapper;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

using Npgsql;

using ProfitFox.MarketData.Presentation;

using Shouldly;

namespace ProfitFox.MarketData.Tests.Integration.Host;

/// <summary>Requirement «Отдельный процесс на своей машине и своей базе», scenario «Первый запуск на пустой базе»: the service migrates its own database at the start.</summary>
public class StartupMigrationTests
{
    [Test]
    public async Task Service_started_on_an_empty_database_creates_its_tables()
    {
        string connectionString = await IntegrationEnvironment.CreateEmptyDatabaseAsync();

        await using WebApplicationFactory<Program> service = Service(connectionString);
        _ = service.Server;

        await using var connection = new NpgsqlConnection(connectionString);
        (await connection.QueryAsync<string>("SELECT tablename FROM pg_tables WHERE schemaname = 'public'")).ShouldContain("candles");
    }

    [Test]
    public async Task Second_start_on_a_migrated_database_changes_nothing()
    {
        string connectionString = await IntegrationEnvironment.CreateEmptyDatabaseAsync();
        await using (WebApplicationFactory<Program> first = Service(connectionString))
        {
            _ = first.Server;
        }

        await using WebApplicationFactory<Program> second = Service(connectionString);
        _ = second.Server;

        await using var connection = new NpgsqlConnection(connectionString);
        (await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM \"VersionInfo\"")).ShouldBe(1);
    }

    private static WebApplicationFactory<Program> Service(string connectionString)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(host => host
            .UseEnvironment("Testing")
            .UseSetting("ConnectionStrings:MarketStore", connectionString));
    }
}
