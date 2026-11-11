using Dapper;

using Npgsql;

using ProfitFox.MarketData.Domain.Entities;
using ProfitFox.MarketData.Domain.Entities.Enums;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories;

/// <summary>
///     The series in their tables and their coverage in <c>coverage</c>. A replace of a period deletes the stored rows of the period, adds the
///     new ones and joins the period into the coverage in one transaction, which an advisory lock of the series keeps one at a time.
/// </summary>
internal sealed partial class MarketStore(NpgsqlDataSource dataSource) : IMarketStore
{
    public async Task<Coverage> GetCoverageAsync(SeriesKey series, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return await ReadCoverageAsync(connection, transaction: null, series, cancellationToken);
    }

    public async Task<SeriesRows> ReadAsync(SeriesKey series, TimeRange period, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        Table table = Table.Of(series);
        var parameters = new
        {
            Category = series.Instrument.Category.ToCode(),
            series.Instrument.Symbol,
            Interval = series.Interval?.ToCode(),
            period.From,
            period.To
        };
        var sql = $"SELECT {table.Columns} FROM {table.Name} WHERE {table.Key} AND {table.Time} >= @From AND {table.Time} < @To ORDER BY {table.Time}";
        var command = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);

        return series.Kind switch
        {
            DataKind.Candles => SeriesRows.OfCandles(
            [
                .. (await connection.QueryAsync<CandleDb>(command)).Select(row => new Candle(
                    row.OpenTime,
                    row.Open,
                    row.High,
                    row.Low,
                    row.Close,
                    row.Volume,
                    row.Turnover))
            ]),
            DataKind.OpenInterest => SeriesRows.OfOpenInterest(
                [.. (await connection.QueryAsync<PointDb>(command)).Select(row => new OpenInterestPoint(row.Time, row.Value))]),
            DataKind.Funding => SeriesRows.OfFunding([.. (await connection.QueryAsync<PointDb>(command)).Select(row => new FundingRatePoint(row.Time, row.Value))]),
            DataKind.Liquidations => SeriesRows.Empty with
            {
                Liquidations =
                [
                    .. (await connection.QueryAsync<LiquidationDb>(command)).Select(row => new LiquidationEvent(
                        row.Time,
                        StoreCodes.ToLiquidationSide(row.Side),
                        row.Price,
                        row.Quantity))
                ]
            },
            _ => SeriesRows.Empty with
            {
                TradeFlow =
                [
                    .. (await connection.QueryAsync<TradeFlowDb>(command)).Select(row => new TradeFlowMinute(
                        row.OpenTime,
                        row.BuyVolume,
                        row.SellVolume,
                        row.BuyTurnover,
                        row.SellTurnover,
                        row.BuyTrades,
                        row.SellTrades))
                ]
            }
        };
    }

    public async Task ReplaceAsync(SeriesKey series, TimeRange period, SeriesRows rows, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        Table table = Table.Of(series);
        string category = series.Instrument.Category.ToCode();
        string interval = series.Interval?.ToCode() ?? "";

        await connection.ExecuteAsync(
            new CommandDefinition("SELECT pg_advisory_xact_lock(hashtext(@Key))", new { Key = LockKey(series) }, transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(
            new CommandDefinition(
                $"DELETE FROM {table.Name} WHERE {table.Key} AND {table.Time} >= @From AND {table.Time} < @To",
                new { Category = category, series.Instrument.Symbol, Interval = interval, period.From, period.To },
                transaction,
                cancellationToken: cancellationToken));

        await using (NpgsqlCommand insert = Insert(connection, series, category, interval, rows))
        {
            insert.Transaction = transaction;
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        Coverage coverage = (await ReadCoverageAsync(connection, transaction, series, cancellationToken)).Add(period);
        await connection.ExecuteAsync(
            new CommandDefinition(
                "DELETE FROM coverage WHERE category = @Category AND symbol = @Symbol AND kind = @Kind AND \"interval\" = @Interval",
                new { Category = category, series.Instrument.Symbol, Kind = series.Kind.ToCode(), Interval = interval },
                transaction,
                cancellationToken: cancellationToken));

        await using (NpgsqlCommand ranges = connection.CreateCommand())
        {
            ranges.Transaction = transaction;
            ranges.CommandText = "INSERT INTO coverage SELECT @category, @symbol, @kind, @interval, r.f, r.t FROM unnest(@froms, @tos) AS r(f, t)";
            ranges.Parameters.AddWithValue("category", category);
            ranges.Parameters.AddWithValue("symbol", series.Instrument.Symbol);
            ranges.Parameters.AddWithValue("kind", series.Kind.ToCode());
            ranges.Parameters.AddWithValue("interval", interval);
            ranges.Parameters.AddWithValue("froms", coverage.Ranges.Select(range => range.From).ToArray());
            ranges.Parameters.AddWithValue("tos", coverage.Ranges.Select(range => range.To).ToArray());
            await ranges.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>The key of the advisory lock that keeps the writes of the coverage of a series one at a time.</summary>
    private static string LockKey(SeriesKey series)
    {
        return $"{series.Instrument.Category.ToCode()}/{series.Instrument.Symbol}/{series.Kind.ToCode()}/{series.Interval?.ToCode() ?? ""}";
    }

    private static async Task<Coverage> ReadCoverageAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        SeriesKey series,
        CancellationToken cancellationToken)
    {
        IEnumerable<RangeDb> ranges = await connection.QueryAsync<RangeDb>(
            new CommandDefinition(
                "SELECT \"from\", \"to\" FROM coverage WHERE category = @Category AND symbol = @Symbol AND kind = @Kind AND \"interval\" = @Interval",
                new { Category = series.Instrument.Category.ToCode(), series.Instrument.Symbol, Kind = series.Kind.ToCode(), Interval = series.Interval?.ToCode() ?? "" },
                transaction,
                cancellationToken: cancellationToken));

        return ranges.Aggregate(Coverage.Empty, (coverage, range) => coverage.Add(new TimeRange(range.From, range.To)));
    }

    // One statement for all rows. Arrays go as Npgsql parameters: Dapper would expand them into IN lists.
    private static NpgsqlCommand Insert(NpgsqlConnection connection, SeriesKey series, string category, string interval, SeriesRows rows)
    {
        NpgsqlCommand command = connection.CreateCommand();
        command.Parameters.AddWithValue("category", category);
        command.Parameters.AddWithValue("symbol", series.Instrument.Symbol);

        switch (series.Kind)
        {
            case DataKind.Candles:
                command.CommandText = """
                                      INSERT INTO candles
                                      SELECT @category, @symbol, @interval, c.* FROM unnest(@times, @opens, @highs, @lows, @closes, @volumes, @turnovers) AS c
                                      """;
                command.Parameters.AddWithValue("interval", interval);
                command.Parameters.AddWithValue("times", rows.Candles.Select(candle => candle.OpenTime).ToArray());
                command.Parameters.AddWithValue("opens", rows.Candles.Select(candle => candle.Open).ToArray());
                command.Parameters.AddWithValue("highs", rows.Candles.Select(candle => candle.High).ToArray());
                command.Parameters.AddWithValue("lows", rows.Candles.Select(candle => candle.Low).ToArray());
                command.Parameters.AddWithValue("closes", rows.Candles.Select(candle => candle.Close).ToArray());
                command.Parameters.AddWithValue("volumes", rows.Candles.Select(candle => candle.Volume).ToArray());
                command.Parameters.AddWithValue("turnovers", rows.Candles.Select(candle => candle.Turnover).ToArray());

                break;
            case DataKind.OpenInterest:
                command.CommandText = "INSERT INTO open_interest SELECT @category, @symbol, p.* FROM unnest(@times, @values) AS p";
                command.Parameters.AddWithValue("times", rows.OpenInterest.Select(point => point.Time).ToArray());
                command.Parameters.AddWithValue("values", rows.OpenInterest.Select(point => point.Value).ToArray());

                break;
            case DataKind.Funding:
                command.CommandText = "INSERT INTO funding_rates SELECT @category, @symbol, r.* FROM unnest(@times, @rates) AS r";
                command.Parameters.AddWithValue("times", rows.Funding.Select(rate => rate.Time).ToArray());
                command.Parameters.AddWithValue("rates", rows.Funding.Select(rate => rate.Rate).ToArray());

                break;
            case DataKind.Liquidations:
                command.CommandText =
                    "INSERT INTO liquidations SELECT @category, @symbol, l.* FROM unnest(@times, @sides, @prices, @quantities) AS l ON CONFLICT DO NOTHING";
                command.Parameters.AddWithValue("times", rows.Liquidations.Select(liquidation => liquidation.Time).ToArray());
                command.Parameters.AddWithValue("sides", rows.Liquidations.Select(liquidation => liquidation.Side.ToCode()).ToArray());
                command.Parameters.AddWithValue("prices", rows.Liquidations.Select(liquidation => liquidation.Price).ToArray());
                command.Parameters.AddWithValue("quantities", rows.Liquidations.Select(liquidation => liquidation.Quantity).ToArray());

                break;
            default:
                command.CommandText = """
                                      INSERT INTO trade_flow_minutes
                                      SELECT @category, @symbol, m.* FROM unnest(@times, @buyVolumes, @sellVolumes, @buyTurnovers, @sellTurnovers, @buyTrades, @sellTrades) AS m
                                      """;
                command.Parameters.AddWithValue("times", rows.TradeFlow.Select(minute => minute.OpenTime).ToArray());
                command.Parameters.AddWithValue("buyVolumes", rows.TradeFlow.Select(minute => minute.BuyVolume).ToArray());
                command.Parameters.AddWithValue("sellVolumes", rows.TradeFlow.Select(minute => minute.SellVolume).ToArray());
                command.Parameters.AddWithValue("buyTurnovers", rows.TradeFlow.Select(minute => minute.BuyTurnover).ToArray());
                command.Parameters.AddWithValue("sellTurnovers", rows.TradeFlow.Select(minute => minute.SellTurnover).ToArray());
                command.Parameters.AddWithValue("buyTrades", rows.TradeFlow.Select(minute => minute.BuyTrades).ToArray());
                command.Parameters.AddWithValue("sellTrades", rows.TradeFlow.Select(minute => minute.SellTrades).ToArray());

                break;
        }

        return command;
    }

    /// <summary>The table of a kind of data: its name, the filter of its series, its column of time and the columns a read takes.</summary>
    private sealed record Table(string Name, string Key, string Time, string Columns)
    {
        private const string Pair = "category = @Category AND symbol = @Symbol";

        public static Table Of(SeriesKey series)
        {
            return series.Kind switch
            {
                DataKind.Candles => new Table(
                    "candles",
                    Pair + " AND \"interval\" = @Interval",
                    "open_time",
                    "open_time AS OpenTime, open, high, low, close, volume, turnover"),
                DataKind.OpenInterest => new Table("open_interest", Pair, "\"time\"", "\"time\", value"),
                DataKind.Funding => new Table("funding_rates", Pair, "\"time\"", "\"time\", rate AS value"),
                DataKind.Liquidations => new Table("liquidations", Pair, "\"time\"", "\"time\", side, price, quantity"),
                _ => new Table(
                    "trade_flow_minutes",
                    Pair,
                    "open_time",
                    "open_time AS OpenTime, buy_volume AS BuyVolume, sell_volume AS SellVolume, buy_turnover AS BuyTurnover, sell_turnover AS SellTurnover, buy_trades AS BuyTrades, sell_trades AS SellTrades")
            };
        }
    }

    private sealed class CandleDb
    {
        public DateTimeOffset OpenTime { get; init; }

        public decimal Open { get; init; }

        public decimal High { get; init; }

        public decimal Low { get; init; }

        public decimal Close { get; init; }

        public decimal Volume { get; init; }

        public decimal Turnover { get; init; }
    }

    /// <summary>A point of open interest or a funding settlement.</summary>
    private sealed class PointDb
    {
        public DateTimeOffset Time { get; init; }

        public decimal Value { get; init; }
    }

    private sealed class TradeFlowDb
    {
        public DateTimeOffset OpenTime { get; init; }

        public decimal BuyVolume { get; init; }

        public decimal SellVolume { get; init; }

        public decimal BuyTurnover { get; init; }

        public decimal SellTurnover { get; init; }

        public int BuyTrades { get; init; }

        public int SellTrades { get; init; }
    }

    private sealed class RangeDb
    {
        public DateTimeOffset From { get; init; }

        public DateTimeOffset To { get; init; }
    }

    private sealed class LiquidationDb
    {
        public DateTimeOffset Time { get; init; }

        public string Side { get; init; } = null!;

        public decimal Price { get; init; }

        public decimal Quantity { get; init; }
    }
}