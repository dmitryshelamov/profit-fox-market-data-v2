using ProfitFox.MarketData.Domain.Entities.Enums;
using ProfitFox.MarketData.Domain.ValueObjects;

using Shouldly;

namespace ProfitFox.MarketData.Tests.Unit.Domain.ValueObjects;

/// <summary>Requirement «Покрытие»: a series is a kind of data of a pair and, for candles, an interval.</summary>
public class SeriesKeyTests
{
    private static readonly Instrument Btc = new Instrument(MarketCategory.Linear, "BTCUSDT");

    [Test]
    public void Candles_of_two_intervals_are_two_series()
    {
        SeriesKey.Candles(Btc, Interval.OneMinute).ShouldNotBe(SeriesKey.Candles(Btc, Interval.OneHour));
    }

    [Test]
    public void Candle_series_keeps_its_interval()
    {
        SeriesKey key = SeriesKey.Candles(Btc, Interval.OneHour);

        key.Kind.ShouldBe(DataKind.Candles);
        key.Instrument.ShouldBe(Btc);
        key.Interval.ShouldBe(Interval.OneHour);
    }

    [TestCase(DataKind.OpenInterest)]
    [TestCase(DataKind.Funding)]
    [TestCase(DataKind.Liquidations)]
    [TestCase(DataKind.TradeFlow)]
    public void Series_other_than_candles_has_no_interval(DataKind kind)
    {
        SeriesKey key = SeriesKey.Of(kind, Btc);

        key.Kind.ShouldBe(kind);
        key.Interval.ShouldBeNull();
    }

    [Test]
    public void Candles_without_an_interval_are_refused()
    {
        Should.Throw<ArgumentException>(() => SeriesKey.Of(DataKind.Candles, Btc));
    }
}