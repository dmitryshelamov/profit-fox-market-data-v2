using Dapper;

using Npgsql;

using Shouldly;

namespace ProfitFox.MarketData.Tests.Integration.Persistence;

/// <summary>Decision 2: the service migrates its own database; the series that grow with time are hypertables compressed after 7 days.</summary>
public class CreateMarketStoreTablesMigrationTests
{
    [Test]
    public async Task Migrations_create_the_tables_of_the_service()
    {
        await using var connection = new NpgsqlConnection(IntegrationEnvironment.ConnectionString);

        IEnumerable<string> tables = await connection.QueryAsync<string>("SELECT tablename FROM pg_tables WHERE schemaname = 'public'");

        tables.ShouldBe(
            ["candles", "collector_state", "coverage", "funding_rates", "liquidations", "open_interest", "pairs", "stream_breaks", "trade_flow_minutes", "VersionInfo"],
            ignoreOrder: true);
    }

    [Test]
    public async Task Series_that_grow_with_time_are_hypertables_compressed_after_seven_days()
    {
        await using var connection = new NpgsqlConnection(IntegrationEnvironment.ConnectionString);

        IEnumerable<string> hypertables = await connection.QueryAsync<string>("SELECT hypertable_name FROM timescaledb_information.hypertables");
        IEnumerable<(string Table, string After)> compressed = await connection.QueryAsync<(string, string)>(
            "SELECT hypertable_name, config ->> 'compress_after' FROM timescaledb_information.jobs WHERE proc_name = 'policy_compression'");

        hypertables.ShouldBe(["candles", "liquidations", "open_interest", "trade_flow_minutes"], ignoreOrder: true);
        compressed.ShouldBe(
            [("candles", "7 days"), ("liquidations", "7 days"), ("open_interest", "7 days"), ("trade_flow_minutes", "7 days")],
            ignoreOrder: true);
    }

    [Test]
    public async Task State_of_the_collector_has_its_one_row()
    {
        await using var connection = new NpgsqlConnection(IntegrationEnvironment.ConnectionString);

        (await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM collector_state")).ShouldBe(1);
    }
}