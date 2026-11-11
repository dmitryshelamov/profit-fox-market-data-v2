using System.Globalization;

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

    // The candle still forming is not closed yet.
    [Test]
    public void Candles_are_closed_up_to_the_start_of_the_forming_one()
    {
        SeriesKey.Candles(Btc, Interval.OneHour).ClosedUntil(Time("2026-11-10T12:34:10Z")).ShouldBe(Time("2026-11-10T12:00:00Z"));
    }

    // The point of the current step of open interest may not be known yet.
    [Test]
    public void Open_interest_is_closed_up_to_the_start_of_the_current_step()
    {
        SeriesKey.Of(DataKind.OpenInterest, Btc).ClosedUntil(Time("2026-11-10T12:34:10Z")).ShouldBe(Time("2026-11-10T12:30:00Z"));
    }

    [TestCase(DataKind.Funding)]
    [TestCase(DataKind.Liquidations)]
    [TestCase(DataKind.TradeFlow)]
    public void Other_series_are_closed_up_to_now(DataKind kind)
    {
        SeriesKey.Of(kind, Btc).ClosedUntil(Time("2026-11-10T12:34:10Z")).ShouldBe(Time("2026-11-10T12:34:10Z"));
    }

    [Test]
    public void Candles_without_an_interval_are_refused()
    {
        Should.Throw<ArgumentException>(() => SeriesKey.Of(DataKind.Candles, Btc));
    }

    private static DateTimeOffset Time(string text)
    {
        return DateTimeOffset.Parse(text, CultureInfo.InvariantCulture);
    }
}