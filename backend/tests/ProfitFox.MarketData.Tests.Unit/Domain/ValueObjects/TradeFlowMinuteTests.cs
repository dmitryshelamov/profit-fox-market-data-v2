using System.Globalization;

using ProfitFox.MarketData.Domain.Entities.Enums;
using ProfitFox.MarketData.Domain.ValueObjects;

using Shouldly;

namespace ProfitFox.MarketData.Tests.Unit.Domain.ValueObjects;

/// <summary>Requirement «Минуты потока сделок»: a trade goes to the minute of its time and to the side of its taker.</summary>
public class TradeFlowMinuteTests
{
    private static readonly DateTimeOffset Minute = DateTimeOffset.Parse("2026-11-09T12:04:00Z", CultureInfo.InvariantCulture);

    // Scenario «Сделки одной минуты».
    [Test]
    public void Trades_of_a_minute_add_up_by_side()
    {
        TradeFlowMinute minute = TradeFlowMinute.Start(new TradeEvent(Minute.AddSeconds(1), TradeSide.Buy, Quantity: 2m, Price: 150m))
            .Add(new TradeEvent(Minute.AddSeconds(2), TradeSide.Buy, Quantity: 1m, Price: 151m))
            .Add(new TradeEvent(Minute.AddSeconds(3), TradeSide.Sell, Quantity: 3m, Price: 149m));

        minute.ShouldBe(new TradeFlowMinute(Minute, BuyVolume: 3m, SellVolume: 3m, BuyTurnover: 451m, SellTurnover: 447m, BuyTrades: 2, SellTrades: 1));
    }

    // Scenario «Сделка на границе минуты».
    [TestCase("2026-11-09T12:04:59.999Z", "2026-11-09T12:04:00Z")]
    [TestCase("2026-11-09T12:05:00.000Z", "2026-11-09T12:05:00Z")]
    public void Trade_goes_to_the_minute_of_its_time(string time, string openTime)
    {
        TradeFlowMinute minute =
            TradeFlowMinute.Start(new TradeEvent(DateTimeOffset.Parse(time, CultureInfo.InvariantCulture), TradeSide.Sell, Quantity: 1m, Price: 10m));

        minute.OpenTime.ShouldBe(DateTimeOffset.Parse(openTime, CultureInfo.InvariantCulture));
    }

    [Test]
    public void Trade_time_with_an_offset_goes_to_its_utc_minute()
    {
        TradeFlowMinute minute = TradeFlowMinute.Start(
            new TradeEvent(DateTimeOffset.Parse("2026-11-09T15:04:30+03:00", CultureInfo.InvariantCulture), TradeSide.Buy, Quantity: 1m, Price: 10m));

        minute.OpenTime.ShouldBe(Minute);
        minute.OpenTime.Offset.ShouldBe(TimeSpan.Zero);
    }
}