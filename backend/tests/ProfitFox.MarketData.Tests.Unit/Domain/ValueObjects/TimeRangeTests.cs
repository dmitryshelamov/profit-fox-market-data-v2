using System.Globalization;

using ProfitFox.MarketData.Domain.ValueObjects;

using Shouldly;

namespace ProfitFox.MarketData.Tests.Unit.Domain.ValueObjects;

public class TimeRangeTests
{
    private static readonly DateTimeOffset Noon = DateTimeOffset.Parse("2026-11-09T12:00:00Z", CultureInfo.InvariantCulture);

    [Test]
    public void Range_keeps_its_start_and_end()
    {
        var range = new TimeRange(Noon, Noon.AddMinutes(5));

        range.From.ShouldBe(Noon);
        range.To.ShouldBe(Noon.AddMinutes(5));
    }

    [Test]
    public void Empty_range_is_refused()
    {
        Should.Throw<ArgumentException>(() => new TimeRange(Noon, Noon));
    }

    [Test]
    public void Range_ending_before_its_start_is_refused()
    {
        Should.Throw<ArgumentException>(() => new TimeRange(Noon, Noon.AddMinutes(-1)));
    }
}