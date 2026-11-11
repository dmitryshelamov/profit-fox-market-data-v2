using Dapper;

using Npgsql;

using ProfitFox.MarketData.Domain.Entities;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories;

internal sealed class MarketPairRepository(NpgsqlDataSource dataSource) : IMarketPairRepository
{
    public async Task<IReadOnlyList<MarketPair>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        IEnumerable<PairDb> pairs = await connection.QueryAsync<PairDb>(
            new CommandDefinition("SELECT category, symbol, listed, listed_at AS ListedAt FROM pairs", cancellationToken: cancellationToken));

        return [.. pairs.Select(ToDomain)];
    }

    public async Task<MarketPair?> FindAsync(Instrument instrument, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var pair = await connection.QuerySingleOrDefaultAsync<PairDb>(
            new CommandDefinition(
                "SELECT category, symbol, listed, listed_at AS ListedAt FROM pairs WHERE category = @Category AND symbol = @Symbol",
                new { Category = instrument.Category.ToCode(), instrument.Symbol },
                cancellationToken: cancellationToken));

        return pair is null ? null : ToDomain(pair);
    }

    public async Task SaveAsync(IReadOnlyCollection<MarketPair> pairs, CancellationToken cancellationToken)
    {
        // One statement for all pairs. Arrays go as Npgsql parameters: Dapper would expand them into IN lists.
        await using NpgsqlCommand command = dataSource.CreateCommand(
            """
            INSERT INTO pairs (category, symbol, listed, listed_at)
            SELECT * FROM unnest(@categories, @symbols, @listed, @listedAt)
            ON CONFLICT (category, symbol) DO UPDATE SET listed = EXCLUDED.listed, listed_at = EXCLUDED.listed_at
            """);
        command.Parameters.AddWithValue("categories", pairs.Select(pair => pair.Instrument.Category.ToCode()).ToArray());
        command.Parameters.AddWithValue("symbols", pairs.Select(pair => pair.Instrument.Symbol).ToArray());
        command.Parameters.AddWithValue("listed", pairs.Select(pair => pair.Listed).ToArray());
        command.Parameters.AddWithValue("listedAt", pairs.Select(pair => pair.ListedAt).ToArray());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static MarketPair ToDomain(PairDb pair)
    {
        return new MarketPair(new Instrument(StoreCodes.ToCategory(pair.Category), pair.Symbol), pair.Listed, pair.ListedAt);
    }

    private sealed class PairDb
    {
        public string Category { get; init; } = null!;

        public string Symbol { get; init; } = null!;

        public bool Listed { get; init; }

        public DateTimeOffset? ListedAt { get; init; }
    }
}