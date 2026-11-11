using System.Globalization;

using ProfitFox.MarketData.Domain.Entities.Enums;

using Shouldly;

namespace ProfitFox.MarketData.Tests.Unit.Domain.Entities.Enums;

public class IntervalExtensionsTests
{
    [TestCase(Interval.OneMinute, "2026-11-09T12:04:00Z", "2026-11-09T12:05:00Z")]
    [TestCase(Interval.ThreeMinutes, "2026-11-09T12:03:00Z", "2026-11-09T12:06:00Z")]
    [TestCase(Interval.FiveMinutes, "2026-11-09T12:05:00Z", "2026-11-09T12:10:00Z")]
    [TestCase(Interval.FifteenMinutes, "2026-11-09T12:15:00Z", "2026-11-09T12:30:00Z")]
    [TestCase(Interval.ThirtyMinutes, "2026-11-09T12:30:00Z", "2026-11-09T13:00:00Z")]
    [TestCase(Interval.OneHour, "2026-11-09T12:00:00Z", "2026-11-09T13:00:00Z")]
    [TestCase(Interval.TwoHours, "2026-11-09T12:00:00Z", "2026-11-09T14:00:00Z")]
    [TestCase(Interval.FourHours, "2026-11-09T12:00:00Z", "2026-11-09T16:00:00Z")]
    [TestCase(Interval.SixHours, "2026-11-09T12:00:00Z", "2026-11-09T18:00:00Z")]
    [TestCase(Interval.TwelveHours, "2026-11-09T12:00:00Z", "2026-11-10T00:00:00Z")]
    [TestCase(Interval.OneDay, "2026-11-09T00:00:00Z", "2026-11-10T00:00:00Z")]
    [TestCase(Interval.OneWeek, "2026-11-09T00:00:00Z", "2026-11-16T00:00:00Z")]
    [TestCase(Interval.OneMonth, "2026-02-01T00:00:00Z", "2026-03-01T00:00:00Z")]
    public void Candle_ends_one_interval_after_its_open(Interval interval, string openTime, string end)
    {
        interval.CandleEnd(Time(openTime)).ShouldBe(Time(end));
    }

    [TestCase(Interval.OneMinute, "2026-11-09T12:04:59.999Z", "2026-11-09T12:04:00Z")]
    [TestCase(Interval.FifteenMinutes, "2026-11-09T12:29:00Z", "2026-11-09T12:15:00Z")]
    [TestCase(Interval.FourHours, "2026-11-09T15:59:00Z", "2026-11-09T12:00:00Z")]
    [TestCase(Interval.OneDay, "2026-11-09T23:00:00Z", "2026-11-09T00:00:00Z")]
    public void Candle_opens_on_the_grid_from_the_epoch(Interval interval, string at, string openTime)
    {
        interval.CandleOpen(Time(at)).ShouldBe(Time(openTime));
    }

    [Test]
    public void Moment_on_a_boundary_opens_its_own_candle()
    {
        Interval.OneHour.CandleOpen(Time("2026-11-09T13:00:00Z")).ShouldBe(Time("2026-11-09T13:00:00Z"));
    }

    [Test]
    public void Week_opens_on_monday()
    {
        // 2026-11-15 is a Sunday.
        Interval.OneWeek.CandleOpen(Time("2026-11-15T22:00:00Z")).ShouldBe(Time("2026-11-09T00:00:00Z"));
    }

    [Test]
    public void Month_opens_on_its_first_day()
    {
        Interval.OneMonth.CandleOpen(Time("2026-11-30T23:59:00Z")).ShouldBe(Time("2026-11-01T00:00:00Z"));
    }

    [Test]
    public void Moment_with_an_offset_is_read_in_utc()
    {
        Interval.OneDay.CandleOpen(Time("2026-11-10T01:00:00+03:00")).ShouldBe(Time("2026-11-09T00:00:00Z"));
    }

    private static DateTimeOffset Time(string text)
    {
        return DateTimeOffset.Parse(text, CultureInfo.InvariantCulture);
    }
}