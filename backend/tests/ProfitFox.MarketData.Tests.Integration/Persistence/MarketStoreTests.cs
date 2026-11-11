using System.Globalization;

using Npgsql;

using ProfitFox.MarketData.Domain.Entities;
using ProfitFox.MarketData.Domain.Entities.Enums;
using ProfitFox.MarketData.Domain.ValueObjects;
using ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entity;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObject;

using Shouldly;

namespace ProfitFox.MarketData.Tests.Integration.Persistence;

/// <summary>Requirements «Покрытие» and «Чтение истории»: the data of a series replaced by period, with its coverage, read back exactly.</summary>
public class MarketStoreTests
{
    private static readonly DateTimeOffset Noon = DateTimeOffset.Parse("2026-11-10T12:00:00Z", CultureInfo.InvariantCulture);
    private static readonly Instrument Btc = new Instrument(MarketCategory.Linear, "BTCUSDT");
    private static readonly SeriesKey Minutes = SeriesKey.Candles(Btc, Interval.OneMinute);

    private MarketStore _store = null!;

    [SetUp]
    public void SetUp()
    {
        _store = new MarketStore(NpgsqlDataSource.Create(IntegrationEnvironment.ConnectionString));
    }

    [Test]
    public async Task Series_without_data_has_no_coverage()
    {
        (await _store.GetCoverageAsync(Minutes, CancellationToken.None)).Ranges.ShouldBeEmpty();
    }

    [Test]
    public async Task Candles_of_a_period_are_stored_exactly_with_the_coverage_of_the_period()
    {
        TimeRange period = Range(fromMinute: 0, toMinute: 3);

        // Reference values: the exactness of numeric is what the test checks, so they stay explicit.
        Candle[] candles =
        [
            new Candle(Noon, Open: 100.5m, High: 101.25m, Low: 0.0000001m, Close: 64000.123456789m, Volume: 12.5m, Turnover: 1250.75m),
            new Candle(Noon.AddMinutes(2), Open: 0.0000001234m, High: 0.0000001235m, Low: 0.0000001233m, Close: 0.0000001234m, Volume: 1000000m, Turnover: 0.1234m)
        ];

        await _store.ReplaceAsync(Minutes, period, SeriesRows.OfCandles(candles), CancellationToken.None);

        (await _store.ReadAsync(Minutes, period, CancellationToken.None)).Candles.ShouldBe(candles);
        (await _store.GetCoverageAsync(Minutes, CancellationToken.None)).Ranges.ShouldBe([period]);
    }

    [Test]
    public async Task Read_gives_only_the_period_oldest_first()
    {
        await _store.ReplaceAsync(
            Minutes,
            Range(fromMinute: 0, toMinute: 5),
            SeriesRows.OfCandles([.. Enumerable.Range(start: 0, count: 5).Reverse().Select(minute => CandleGenerator.Generate(Noon.AddMinutes(minute)))]),
            CancellationToken.None);

        (await _store.ReadAsync(Minutes, Range(fromMinute: 1, toMinute: 3), CancellationToken.None)).Candles.Select(candle => candle.OpenTime)
            .ShouldBe([Noon.AddMinutes(1), Noon.AddMinutes(2)]);
    }

    // Scenario «Пересборка дня»: the period is replaced whole, a row the new data does not have is gone; the rest of the series stays.
    [Test]
    public async Task Period_written_again_is_replaced_whole_and_the_rest_stays()
    {
        Candle first = CandleGenerator.Generate(Noon);
        Candle replaced = CandleGenerator.Generate(Noon.AddMinutes(2));
        Candle last = CandleGenerator.Generate(Noon.AddMinutes(3));
        await _store.ReplaceAsync(
            Minutes,
            Range(fromMinute: 0, toMinute: 4),
            SeriesRows.OfCandles([first, CandleGenerator.Generate(Noon.AddMinutes(1)), last]),
            CancellationToken.None);

        await _store.ReplaceAsync(Minutes, Range(fromMinute: 1, toMinute: 3), SeriesRows.OfCandles([replaced]), CancellationToken.None);

        (await _store.ReadAsync(Minutes, Range(fromMinute: 0, toMinute: 4), CancellationToken.None)).Candles.ShouldBe([first, replaced, last]);
    }

    [Test]
    public async Task Touching_periods_join_in_the_coverage_and_a_separate_one_stays_apart()
    {
        await _store.ReplaceAsync(Minutes, Range(fromMinute: 0, toMinute: 2), SeriesRows.Empty, CancellationToken.None);
        await _store.ReplaceAsync(Minutes, Range(fromMinute: 2, toMinute: 4), SeriesRows.Empty, CancellationToken.None);
        await _store.ReplaceAsync(Minutes, Range(fromMinute: 10, toMinute: 12), SeriesRows.Empty, CancellationToken.None);

        (await _store.GetCoverageAsync(Minutes, CancellationToken.None)).Ranges.ShouldBe([Range(fromMinute: 0, toMinute: 4), Range(fromMinute: 10, toMinute: 12)]);
    }

    [Test]
    public async Task Series_of_another_interval_pair_or_kind_are_apart()
    {
        await _store.ReplaceAsync(Minutes, Range(fromMinute: 0, toMinute: 2), SeriesRows.OfCandles([CandleGenerator.Generate(Noon)]), CancellationToken.None);
        SeriesKey hours = SeriesKey.Candles(Btc, Interval.OneHour);
        SeriesKey spot = SeriesKey.Candles(new Instrument(MarketCategory.Spot, "BTCUSDT"), Interval.OneMinute);
        SeriesKey openInterest = SeriesKey.Of(DataKind.OpenInterest, Btc);

        foreach (SeriesKey other in new[] { hours, spot, openInterest })
        {
            (await _store.GetCoverageAsync(other, CancellationToken.None)).Ranges.ShouldBeEmpty();
            (await _store.ReadAsync(other, Range(fromMinute: 0, toMinute: 2), CancellationToken.None)).ShouldSatisfyAllConditions(
                rows => rows.Candles.ShouldBeEmpty(),
                rows => rows.OpenInterest.ShouldBeEmpty());
        }
    }

    [Test]
    public async Task Open_interest_and_funding_are_stored_exactly()
    {
        SeriesKey openInterest = SeriesKey.Of(DataKind.OpenInterest, Btc);
        SeriesKey funding = SeriesKey.Of(DataKind.Funding, Btc);
        OpenInterestPoint[] points = [new OpenInterestPoint(Noon, Value: 1234567.89m), new OpenInterestPoint(Noon.AddMinutes(5), Value: 1234568.01m)];
        FundingRatePoint[] rates = [new FundingRatePoint(Noon, Rate: -0.00012345m)];

        await _store.ReplaceAsync(openInterest, Range(fromMinute: 0, toMinute: 10), SeriesRows.OfOpenInterest(points), CancellationToken.None);
        await _store.ReplaceAsync(funding, Range(fromMinute: 0, toMinute: 10), SeriesRows.OfFunding(rates), CancellationToken.None);

        (await _store.ReadAsync(openInterest, Range(fromMinute: 0, toMinute: 10), CancellationToken.None)).OpenInterest.ShouldBe(points);
        (await _store.ReadAsync(funding, Range(fromMinute: 0, toMinute: 10), CancellationToken.None)).Funding.ShouldBe(rates);
    }

    [Test]
    public async Task Liquidations_and_minutes_of_trades_are_stored_exactly()
    {
        SeriesKey liquidations = SeriesKey.Of(DataKind.Liquidations, Btc);
        SeriesKey trades = SeriesKey.Of(DataKind.TradeFlow, Btc);
        LiquidationEvent[] events = [LiquidationEventGenerator.Generate(Noon.AddMilliseconds(250)), LiquidationEventGenerator.Generate(Noon.AddSeconds(3))];
        TradeFlowMinute[] minutes = [TradeFlowMinuteGenerator.Generate(Noon)];

        await _store.ReplaceAsync(liquidations, Range(fromMinute: 0, toMinute: 1), SeriesRows.Empty with { Liquidations = events }, CancellationToken.None);
        await _store.ReplaceAsync(trades, Range(fromMinute: 0, toMinute: 1), SeriesRows.Empty with { TradeFlow = minutes }, CancellationToken.None);

        (await _store.ReadAsync(liquidations, Range(fromMinute: 0, toMinute: 1), CancellationToken.None)).Liquidations.ShouldBe(events);
        (await _store.ReadAsync(trades, Range(fromMinute: 0, toMinute: 1), CancellationToken.None)).TradeFlow.ShouldBe(minutes);
    }

    private static TimeRange Range(int fromMinute, int toMinute)
    {
        return new TimeRange(Noon.AddMinutes(fromMinute), Noon.AddMinutes(toMinute));
    }
}