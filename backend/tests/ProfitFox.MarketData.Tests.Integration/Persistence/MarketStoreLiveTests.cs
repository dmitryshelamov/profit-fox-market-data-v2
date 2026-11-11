using System.Globalization;

using Dapper;

using Npgsql;

using ProfitFox.MarketData.Domain.Entities;
using ProfitFox.MarketData.Domain.Entities.Enums;
using ProfitFox.MarketData.Domain.ValueObjects;
using ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entity;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObject;

using Shouldly;

namespace ProfitFox.MarketData.Tests.Integration.Persistence;

/// <summary>Decisions 3 and 6: the batch of the live lane in one transaction, with the coverage it gives and the breaks of the streams.</summary>
public class MarketStoreLiveTests
{
    private static readonly DateTimeOffset Minute = DateTimeOffset.Parse("2026-11-11T12:04:00Z", CultureInfo.InvariantCulture);
    private static readonly Instrument Btc = new Instrument(MarketCategory.Linear, "BTCUSDT");
    private static readonly Instrument Sol = new Instrument(MarketCategory.Spot, "SOLUSDT");

    private MarketStore _store = null!;

    [SetUp]
    public void SetUp()
    {
        _store = new MarketStore(NpgsqlDataSource.Create(IntegrationEnvironment.ConnectionString));
    }

    // Scenario «Минута закрылась»: the closed candles of every pair go in one batch with the coverage of their minutes.
    [Test]
    public async Task Closed_minutes_of_several_pairs_are_stored_with_their_coverage()
    {
        Candle btc = CandleGenerator.Generate(Minute);
        Candle sol = CandleGenerator.Generate(Minute);

        await _store.WriteLiveAsync(
            LiveBatch.Empty with { Candles = [(Btc, btc), (Sol, sol)], Coverage = [(MinutesOf(Btc), MinuteRange(0)), (MinutesOf(Sol), MinuteRange(0))] },
            CancellationToken.None);

        (await _store.ReadAsync(MinutesOf(Btc), MinuteRange(0), CancellationToken.None)).Candles.ShouldBe([btc]);
        (await _store.ReadAsync(MinutesOf(Sol), MinuteRange(0), CancellationToken.None)).Candles.ShouldBe([sol]);
        (await _store.GetCoverageAsync(MinutesOf(Btc), CancellationToken.None)).Ranges.ShouldBe([MinuteRange(0)]);
        (await _store.GetCoverageAsync(MinutesOf(Sol), CancellationToken.None)).Ranges.ShouldBe([MinuteRange(0)]);
    }

    // Requirement «Живые минутные свечи»: a repeated write of the same candle replaces it.
    [Test]
    public async Task Candle_written_again_replaces_the_stored_one()
    {
        Candle replacement = CandleGenerator.Generate(Minute);
        await _store.WriteLiveAsync(LiveBatch.Empty with { Candles = [(Btc, CandleGenerator.Generate(Minute))] }, CancellationToken.None);

        await _store.WriteLiveAsync(LiveBatch.Empty with { Candles = [(Btc, replacement)] }, CancellationToken.None);

        (await _store.ReadAsync(MinutesOf(Btc), MinuteRange(0), CancellationToken.None)).Candles.ShouldBe([replacement]);
    }

    // A batch that failed goes again with the next one, so a minute can come twice in a batch.
    [Test]
    public async Task Later_candle_of_a_minute_in_one_batch_wins()
    {
        Candle later = CandleGenerator.Generate(Minute);

        await _store.WriteLiveAsync(LiveBatch.Empty with { Candles = [(Btc, CandleGenerator.Generate(Minute)), (Btc, later)] }, CancellationToken.None);

        (await _store.ReadAsync(MinutesOf(Btc), MinuteRange(0), CancellationToken.None)).Candles.ShouldBe([later]);
    }

    [Test]
    public async Task Later_minute_of_trades_in_one_batch_wins()
    {
        TradeFlowMinute later = TradeFlowMinuteGenerator.Generate(Minute);

        await _store.WriteLiveAsync(LiveBatch.Empty with { TradeMinutes = [(Btc, TradeFlowMinuteGenerator.Generate(Minute)), (Btc, later)] }, CancellationToken.None);

        (await _store.ReadAsync(SeriesKey.Of(DataKind.TradeFlow, Btc), MinuteRange(0), CancellationToken.None)).TradeFlow.ShouldBe([later]);
    }

    // Decision 8: adjacent ranges join, so the coverage of a series stays its disjoint ranges.
    [Test]
    public async Task Live_minute_joins_the_stored_coverage()
    {
        await _store.ReplaceAsync(MinutesOf(Btc), new TimeRange(Minute.AddMinutes(-3), Minute), SeriesRows.Empty, CancellationToken.None);

        await _store.WriteLiveAsync(LiveBatch.Empty with { Coverage = [(MinutesOf(Btc), MinuteRange(0))] }, CancellationToken.None);

        (await _store.GetCoverageAsync(MinutesOf(Btc), CancellationToken.None)).Ranges.ShouldBe([new TimeRange(Minute.AddMinutes(-3), Minute.AddMinutes(1))]);
    }

    [Test]
    public async Task Periods_of_one_series_in_a_batch_join_too()
    {
        await _store.WriteLiveAsync(
            LiveBatch.Empty with { Coverage = [(MinutesOf(Btc), MinuteRange(1)), (MinutesOf(Btc), MinuteRange(0)), (MinutesOf(Btc), MinuteRange(3))] },
            CancellationToken.None);

        (await _store.GetCoverageAsync(MinutesOf(Btc), CancellationToken.None)).Ranges
            .ShouldBe([new TimeRange(Minute, Minute.AddMinutes(2)), MinuteRange(3)]);
    }

    // A replace of a period rewrites the whole coverage of its series under the advisory lock of the series: the live lane waits for it, so
    // neither loses the coverage of the other.
    [Test]
    public async Task Live_coverage_waits_for_the_lock_of_its_series()
    {
        await using var replace = new NpgsqlConnection(IntegrationEnvironment.ConnectionString);
        await replace.OpenAsync();
        await using NpgsqlTransaction transaction = await replace.BeginTransactionAsync();
        await replace.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtext('linear/BTCUSDT/candles/1'))", transaction: transaction);

        Task write = _store.WriteLiveAsync(LiveBatch.Empty with { Coverage = [(MinutesOf(Btc), MinuteRange(0))] }, CancellationToken.None);

        (await Task.WhenAny(write, Task.Delay(TimeSpan.FromMilliseconds(500)))).ShouldNotBe(write);
        await transaction.CommitAsync();
        await write;
    }

    // Scenario «Сделки одной минуты»: a minute of trades written again replaces the stored one and gives no coverage.
    [Test]
    public async Task Minute_of_trades_is_stored_and_replaced_without_coverage()
    {
        TradeFlowMinute replacement = TradeFlowMinuteGenerator.Generate(Minute);
        await _store.WriteLiveAsync(LiveBatch.Empty with { TradeMinutes = [(Btc, TradeFlowMinuteGenerator.Generate(Minute))] }, CancellationToken.None);

        await _store.WriteLiveAsync(LiveBatch.Empty with { TradeMinutes = [(Btc, replacement)] }, CancellationToken.None);

        SeriesKey trades = SeriesKey.Of(DataKind.TradeFlow, Btc);
        (await _store.ReadAsync(trades, MinuteRange(0), CancellationToken.None)).TradeFlow.ShouldBe([replacement]);
        (await _store.GetCoverageAsync(trades, CancellationToken.None)).Ranges.ShouldBeEmpty();
    }

    // Requirement «Ликвидации»: a repeated write of the same liquidation gives no second row.
    [Test]
    public async Task Liquidation_written_twice_is_stored_once_with_the_coverage_of_the_batch()
    {
        LiquidationEvent liquidation = LiquidationEventGenerator.Generate(Minute.AddSeconds(31.25));
        SeriesKey liquidations = SeriesKey.Of(DataKind.Liquidations, Btc);
        await _store.WriteLiveAsync(LiveBatch.Empty with { Liquidations = [(Btc, liquidation)] }, CancellationToken.None);

        await _store.WriteLiveAsync(LiveBatch.Empty with { Liquidations = [(Btc, liquidation)], Coverage = [(liquidations, MinuteRange(0))] }, CancellationToken.None);

        (await _store.ReadAsync(liquidations, MinuteRange(0), CancellationToken.None)).Liquidations.ShouldBe([liquidation]);
        (await _store.GetCoverageAsync(liquidations, CancellationToken.None)).Ranges.ShouldBe([MinuteRange(0)]);
    }

    // Scenario «Соединение восстановилось»; the names of the streams are those of the metrics.
    [TestCase(LiveStream.Candles, "candles")]
    [TestCase(LiveStream.Trades, "trades")]
    [TestCase(LiveStream.Liquidations, "liquidations")]
    public async Task Break_of_a_stream_is_stored_once(LiveStream stream, string name)
    {
        var streamBreak = new StreamBreak(stream, Minute.AddSeconds(5), Minute.AddSeconds(41));
        await _store.WriteLiveAsync(LiveBatch.Empty with { Breaks = [streamBreak] }, CancellationToken.None);

        await _store.WriteLiveAsync(LiveBatch.Empty with { Breaks = [streamBreak] }, CancellationToken.None);

        await using var connection = new NpgsqlConnection(IntegrationEnvironment.ConnectionString);
        IEnumerable<(string, DateTimeOffset, DateTimeOffset)> breaks =
            await connection.QueryAsync<(string, DateTimeOffset, DateTimeOffset)>("SELECT stream, lost_at, restored_at FROM stream_breaks");
        breaks.ShouldBe([(name, Minute.AddSeconds(5), Minute.AddSeconds(41))]);
    }

    [Test]
    public async Task Without_minute_candles_there_is_no_last_minute()
    {
        await _store.ReplaceAsync(SeriesKey.Candles(Btc, Interval.OneHour), new TimeRange(Minute, Minute.AddHours(1)), SeriesRows.OfCandles([CandleGenerator.Generate(Minute)]), CancellationToken.None);

        (await _store.GetLastMinuteEndAsync(CancellationToken.None)).ShouldBeNull();
    }

    // Scenario «Перезапуск сервиса»: the break after a restart starts at the end of the last written minute of any pair.
    [Test]
    public async Task Last_minute_ends_after_the_latest_minute_candle_of_any_pair()
    {
        await _store.WriteLiveAsync(
            LiveBatch.Empty with { Candles = [(Btc, CandleGenerator.Generate(Minute.AddMinutes(-3))), (Sol, CandleGenerator.Generate(Minute)), (Btc, CandleGenerator.Generate(Minute.AddMinutes(-1)))] },
            CancellationToken.None);

        (await _store.GetLastMinuteEndAsync(CancellationToken.None)).ShouldBe(Minute.AddMinutes(1));
    }

    private static SeriesKey MinutesOf(Instrument instrument)
    {
        return SeriesKey.Candles(instrument, Interval.OneMinute);
    }

    private static TimeRange MinuteRange(int minute)
    {
        return new TimeRange(Minute.AddMinutes(minute), Minute.AddMinutes(minute + 1));
    }
}
