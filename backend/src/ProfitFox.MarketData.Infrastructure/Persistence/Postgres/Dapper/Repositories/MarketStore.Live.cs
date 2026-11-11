using Npgsql;

using ProfitFox.MarketData.Domain.Entities;
using ProfitFox.MarketData.Domain.Entities.Enums;
using ProfitFox.MarketData.Domain.ValueObjects;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories;

/// <summary>
///     The writes of the live lane: every kind of the batch in one statement, then the coverage of the series of the batch read, joined
///     with its periods and written back, under the advisory locks a replace of a period takes, so neither loses the coverage of the other.
/// </summary>
internal sealed partial class MarketStore
{
    public async Task WriteLiveAsync(LiveBatch batch, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        await ExecuteAsync(connection, transaction, CandlesUpsert(batch), cancellationToken);
        await ExecuteAsync(connection, transaction, TradeMinutesUpsert(batch), cancellationToken);
        await ExecuteAsync(connection, transaction, LiquidationsInsert(batch), cancellationToken);
        await ExecuteAsync(connection, transaction, BreaksInsert(batch), cancellationToken);
        await CoverAsync(connection, transaction, batch.Coverage, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<DateTimeOffset?> GetLastMinuteEndAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        await using var command = new NpgsqlCommand("SELECT open_time FROM candles WHERE \"interval\" = @interval ORDER BY open_time DESC LIMIT 1", connection);
        command.Parameters.AddWithValue("interval", Interval.OneMinute.ToCode());
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        DateTimeOffset? last = await reader.ReadAsync(cancellationToken) ? reader.GetFieldValue<DateTimeOffset>(0) : null;

        return last?.AddMinutes(1);
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, NpgsqlCommand command, CancellationToken cancellationToken)
    {
        await using (command)
        {
            command.Connection = connection;
            command.Transaction = transaction;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    // One row per minute in a statement: an upsert cannot touch a row twice, so the later candle of a minute wins.
    private static NpgsqlCommand CandlesUpsert(LiveBatch batch)
    {
        (Instrument Instrument, Candle Candle)[] candles =
            [.. batch.Candles.GroupBy(item => (item.Instrument, item.Candle.OpenTime)).Select(minute => minute.Last())];
        var command = new NpgsqlCommand(
            """
            INSERT INTO candles
            SELECT c.category, c.symbol, @interval, c.open_time, c.open, c.high, c.low, c.close, c.volume, c.turnover
            FROM unnest(@categories, @symbols, @times, @opens, @highs, @lows, @closes, @volumes, @turnovers)
                AS c(category, symbol, open_time, open, high, low, close, volume, turnover)
            ON CONFLICT (category, symbol, "interval", open_time) DO UPDATE SET
                open = excluded.open, high = excluded.high, low = excluded.low, close = excluded.close, volume = excluded.volume, turnover = excluded.turnover
            """);
        command.Parameters.AddWithValue("interval", Interval.OneMinute.ToCode());
        AddPairs(command, candles.Select(item => item.Instrument));
        command.Parameters.AddWithValue("times", candles.Select(item => item.Candle.OpenTime).ToArray());
        command.Parameters.AddWithValue("opens", candles.Select(item => item.Candle.Open).ToArray());
        command.Parameters.AddWithValue("highs", candles.Select(item => item.Candle.High).ToArray());
        command.Parameters.AddWithValue("lows", candles.Select(item => item.Candle.Low).ToArray());
        command.Parameters.AddWithValue("closes", candles.Select(item => item.Candle.Close).ToArray());
        command.Parameters.AddWithValue("volumes", candles.Select(item => item.Candle.Volume).ToArray());
        command.Parameters.AddWithValue("turnovers", candles.Select(item => item.Candle.Turnover).ToArray());

        return command;
    }

    private static NpgsqlCommand TradeMinutesUpsert(LiveBatch batch)
    {
        (Instrument Instrument, TradeFlowMinute Minute)[] minutes =
            [.. batch.TradeMinutes.GroupBy(item => (item.Instrument, item.Minute.OpenTime)).Select(minute => minute.Last())];
        var command = new NpgsqlCommand(
            """
            INSERT INTO trade_flow_minutes
            SELECT * FROM unnest(@categories, @symbols, @times, @buyVolumes, @sellVolumes, @buyTurnovers, @sellTurnovers, @buyTrades, @sellTrades)
            ON CONFLICT (category, symbol, open_time) DO UPDATE SET
                buy_volume = excluded.buy_volume, sell_volume = excluded.sell_volume, buy_turnover = excluded.buy_turnover,
                sell_turnover = excluded.sell_turnover, buy_trades = excluded.buy_trades, sell_trades = excluded.sell_trades
            """);
        AddPairs(command, minutes.Select(item => item.Instrument));
        command.Parameters.AddWithValue("times", minutes.Select(item => item.Minute.OpenTime).ToArray());
        command.Parameters.AddWithValue("buyVolumes", minutes.Select(item => item.Minute.BuyVolume).ToArray());
        command.Parameters.AddWithValue("sellVolumes", minutes.Select(item => item.Minute.SellVolume).ToArray());
        command.Parameters.AddWithValue("buyTurnovers", minutes.Select(item => item.Minute.BuyTurnover).ToArray());
        command.Parameters.AddWithValue("sellTurnovers", minutes.Select(item => item.Minute.SellTurnover).ToArray());
        command.Parameters.AddWithValue("buyTrades", minutes.Select(item => item.Minute.BuyTrades).ToArray());
        command.Parameters.AddWithValue("sellTrades", minutes.Select(item => item.Minute.SellTrades).ToArray());

        return command;
    }

    private static NpgsqlCommand LiquidationsInsert(LiveBatch batch)
    {
        var command = new NpgsqlCommand(
            "INSERT INTO liquidations SELECT * FROM unnest(@categories, @symbols, @times, @sides, @prices, @quantities) ON CONFLICT DO NOTHING");
        AddPairs(command, batch.Liquidations.Select(item => item.Instrument));
        command.Parameters.AddWithValue("times", batch.Liquidations.Select(item => item.Liquidation.Time).ToArray());
        command.Parameters.AddWithValue("sides", batch.Liquidations.Select(item => item.Liquidation.Side.ToCode()).ToArray());
        command.Parameters.AddWithValue("prices", batch.Liquidations.Select(item => item.Liquidation.Price).ToArray());
        command.Parameters.AddWithValue("quantities", batch.Liquidations.Select(item => item.Liquidation.Quantity).ToArray());

        return command;
    }

    private static NpgsqlCommand BreaksInsert(LiveBatch batch)
    {
        var command = new NpgsqlCommand("INSERT INTO stream_breaks SELECT * FROM unnest(@streams, @lostAt, @restoredAt) ON CONFLICT DO NOTHING");
        command.Parameters.AddWithValue("streams", batch.Breaks.Select(streamBreak => streamBreak.Stream.ToCode()).ToArray());
        command.Parameters.AddWithValue("lostAt", batch.Breaks.Select(streamBreak => streamBreak.LostAt).ToArray());
        command.Parameters.AddWithValue("restoredAt", batch.Breaks.Select(streamBreak => streamBreak.RestoredAt).ToArray());

        return command;
    }

    private static void AddPairs(NpgsqlCommand command, IEnumerable<Instrument> instruments)
    {
        Instrument[] pairs = [.. instruments];
        command.Parameters.AddWithValue("categories", pairs.Select(instrument => instrument.Category.ToCode()).ToArray());
        command.Parameters.AddWithValue("symbols", pairs.Select(instrument => instrument.Symbol).ToArray());
    }

    /// <summary>
    ///     The locks go in the order of their keys, so two writers of several series never wait for each other in a circle; a replace takes
    ///     the lock of one series only.
    /// </summary>
    private static async Task CoverAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IReadOnlyList<(SeriesKey Series, TimeRange Period)> periods,
        CancellationToken cancellationToken)
    {
        Dictionary<SeriesKey, Coverage> coverage = periods
            .GroupBy(item => item.Series)
            .ToDictionary(series => series.Key, series => series.Aggregate(Coverage.Empty, (joined, item) => joined.Add(item.Period)));
        SeriesKey[] series = [.. coverage.Keys.OrderBy(LockKey, StringComparer.Ordinal)];

        var locks = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtext(k)) FROM unnest(@keys) WITH ORDINALITY AS l(k, n) ORDER BY n");
        locks.Parameters.AddWithValue("keys", series.Select(LockKey).ToArray());
        await ExecuteAsync(connection, transaction, locks, cancellationToken);

        await using (NpgsqlCommand stored = SeriesCommand(
                         series,
                         """
                         SELECT c.category, c.symbol, c.kind, c."interval", c."from", c."to"
                         FROM coverage c JOIN unnest(@categories, @symbols, @kinds, @intervals) AS s(category, symbol, kind, "interval")
                             USING (category, symbol, kind, "interval")
                         """))
        {
            stored.Connection = connection;
            stored.Transaction = transaction;
            Dictionary<string, SeriesKey> byKey = series.ToDictionary(LockKey);
            var ranges = new List<(SeriesKey Series, TimeRange Range)>();

            await using (NpgsqlDataReader reader = await stored.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    string key = $"{reader.GetString(0)}/{reader.GetString(1)}/{reader.GetString(2)}/{reader.GetString(3)}";
                    ranges.Add((byKey[key], new TimeRange(reader.GetFieldValue<DateTimeOffset>(4), reader.GetFieldValue<DateTimeOffset>(5))));
                }
            }

            foreach ((SeriesKey key, TimeRange range) in ranges)
            {
                coverage[key] = coverage[key].Add(range);
            }
        }

        await ExecuteAsync(
            connection,
            transaction,
            SeriesCommand(
                series,
                """
                DELETE FROM coverage c USING unnest(@categories, @symbols, @kinds, @intervals) AS s(category, symbol, kind, "interval")
                WHERE c.category = s.category AND c.symbol = s.symbol AND c.kind = s.kind AND c."interval" = s."interval"
                """),
            cancellationToken);

        (SeriesKey Series, TimeRange Range)[] joined = [.. coverage.SelectMany(entry => entry.Value.Ranges.Select(range => (entry.Key, range)))];
        NpgsqlCommand insert = SeriesCommand(
            [.. joined.Select(item => item.Series)],
            "INSERT INTO coverage SELECT * FROM unnest(@categories, @symbols, @kinds, @intervals, @froms, @tos)");
        insert.Parameters.AddWithValue("froms", joined.Select(item => item.Range.From).ToArray());
        insert.Parameters.AddWithValue("tos", joined.Select(item => item.Range.To).ToArray());
        await ExecuteAsync(connection, transaction, insert, cancellationToken);
    }

    private static NpgsqlCommand SeriesCommand(IReadOnlyList<SeriesKey> series, string sql)
    {
        var command = new NpgsqlCommand(sql);
        command.Parameters.AddWithValue("categories", series.Select(key => key.Instrument.Category.ToCode()).ToArray());
        command.Parameters.AddWithValue("symbols", series.Select(key => key.Instrument.Symbol).ToArray());
        command.Parameters.AddWithValue("kinds", series.Select(key => key.Kind.ToCode()).ToArray());
        command.Parameters.AddWithValue("intervals", series.Select(key => key.Interval?.ToCode() ?? "").ToArray());

        return command;
    }
}
