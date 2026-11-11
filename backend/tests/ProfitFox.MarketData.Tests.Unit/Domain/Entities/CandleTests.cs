using System.Globalization;

using ProfitFox.MarketData.Domain.Entities;
using ProfitFox.MarketData.Domain.Entities.Enums;

using Shouldly;

namespace ProfitFox.MarketData.Tests.Unit.Domain.Entities;

public class CandleTests
{
    private static readonly DateTimeOffset Open = DateTimeOffset.Parse("2026-11-09T12:00:00Z", CultureInfo.InvariantCulture);
    private static readonly Candle Hour = new Candle(Open, Open: 100m, High: 110m, Low: 90m, Close: 105m, Volume: 3m, Turnover: 315m);

    [Test]
    public void Candle_is_closed_at_its_end()
    {
        Hour.IsClosedAt(Interval.OneHour, Open.AddHours(1)).ShouldBeTrue();
    }

    [Test]
    public void Candle_is_forming_before_its_end()
    {
        Hour.IsClosedAt(Interval.OneHour, Open.AddMinutes(59).AddSeconds(59)).ShouldBeFalse();
    }
}