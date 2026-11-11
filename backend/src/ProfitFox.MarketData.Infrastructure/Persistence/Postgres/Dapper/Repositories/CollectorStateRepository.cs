using Dapper;

using Npgsql;

using ProfitFox.MarketData.Infrastructure.Exchanges;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories;

/// <summary>The one row of the state of the collector: for now, the end of the pause of the REST requests.</summary>
internal sealed class CollectorStateRepository(NpgsqlDataSource dataSource) : IRestPauseStore
{
    public async Task<DateTimeOffset?> GetPausedUntilAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        var state = await connection.QuerySingleAsync<StateDb>(
            new CommandDefinition("SELECT rest_paused_until AS RestPausedUntil FROM collector_state WHERE id = 1", cancellationToken: cancellationToken));

        return state.RestPausedUntil;
    }

    public async Task PauseAsync(DateTimeOffset until, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(
            new CommandDefinition(
                "UPDATE collector_state SET rest_paused_until = GREATEST(rest_paused_until, @Until) WHERE id = 1",
                new { Until = until },
                cancellationToken: cancellationToken));
    }

    private sealed class StateDb
    {
        public DateTimeOffset? RestPausedUntil { get; init; }
    }
}