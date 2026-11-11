using System.Globalization;

using Microsoft.Extensions.Time.Testing;

using ProfitFox.MarketData.Application.Services.Collection;
using ProfitFox.MarketData.Domain.Entities.Enums;
using ProfitFox.MarketData.Domain.ValueObjects;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObject;

using Shouldly;

namespace ProfitFox.MarketData.Tests.Unit.Application.Services.Collection;

/// <summary>Requirement «Минуты потока сделок»: the trades of a pair add up in the minute of their time and go out 5 seconds after its end.</summary>
public class TradeMinutesServiceTests
{
    private static readonly DateTimeOffset Minute = DateTimeOffset.Parse("2026-11-09T12:04:00Z", CultureInfo.InvariantCulture);
    private static readonly Instrument Btc = new Instrument(MarketCategory.Linear, "BTCUSDT");
    private static readonly Instrument Sol = new Instrument(MarketCategory.Linear, "SOLUSDT");

    private FakeTimeProvider _time = null!;
    private TradeMinutesService _minutes = null!;

    [SetUp]
    public void SetUp()
    {
        _time = new FakeTimeProvider(Minute.AddSeconds(10));
        _minutes = new TradeMinutesService(_time);
    }

    // Scenario «Сделки одной минуты».
    [Test]
    public void Minute_goes_out_five_seconds_after_its_end_with_its_trades_added_up()
    {
        _minutes.Add(Btc, new TradeEvent(Minute.AddSeconds(1), TradeSide.Buy, Quantity: 2m, Price: 150m));
        _minutes.Add(Btc, new TradeEvent(Minute.AddSeconds(30), TradeSide.Sell, Quantity: 3m, Price: 149m));

        _time.SetUtcNow(Minute.AddSeconds(64));
        _minutes.TakeClosed().ShouldBeEmpty();

        _time.SetUtcNow(Minute.AddSeconds(65));
        _minutes.TakeClosed()
            .ShouldBe([(Btc, new TradeFlowMinute(Minute, BuyVolume: 2m, SellVolume: 3m, BuyTurnover: 300m, SellTurnover: 447m, BuyTrades: 1, SellTrades: 1))]);
    }

    [Test]
    public void Minutes_of_every_pair_go_out_together()
    {
        _minutes.Add(Btc, TradeEventGenerator.Generate(Minute.AddSeconds(1)));
        _minutes.Add(Sol, TradeEventGenerator.Generate(Minute.AddSeconds(2)));

        _time.SetUtcNow(Minute.AddSeconds(65));

        _minutes.TakeClosed().Select(entry => entry.Instrument).ShouldBe([Btc, Sol], ignoreOrder: true);
    }

    [Test]
    public void Minute_that_went_out_does_not_go_out_again()
    {
        _minutes.Add(Btc, new TradeEvent(Minute.AddSeconds(1), TradeSide.Buy, Quantity: 1m, Price: 150m));
        _time.SetUtcNow(Minute.AddSeconds(65));
        _minutes.TakeClosed();

        _minutes.TakeClosed().ShouldBeEmpty();
    }

    [Test]
    public void Minute_still_open_stays()
    {
        _minutes.Add(Btc, new TradeEvent(Minute.AddSeconds(1), TradeSide.Buy, Quantity: 1m, Price: 150m));
        _time.SetUtcNow(Minute.AddSeconds(65));
        _minutes.Add(Btc, new TradeEvent(Minute.AddSeconds(62), TradeSide.Sell, Quantity: 1m, Price: 151m));

        _minutes.TakeClosed().Single().Minute.OpenTime.ShouldBe(Minute);

        _time.SetUtcNow(Minute.AddSeconds(125));
        _minutes.TakeClosed().Single().Minute.OpenTime.ShouldBe(Minute.AddMinutes(1));
    }

    // The rebuild of the day from the archive brings such a trade back.
    [Test]
    public void Trade_of_a_minute_that_went_out_is_dropped()
    {
        _minutes.Add(Btc, new TradeEvent(Minute.AddSeconds(1), TradeSide.Buy, Quantity: 1m, Price: 150m));
        _time.SetUtcNow(Minute.AddSeconds(65));
        _minutes.TakeClosed();

        _minutes.Add(Btc, new TradeEvent(Minute.AddSeconds(59), TradeSide.Sell, Quantity: 1m, Price: 150m)).ShouldBeNull();

        _time.SetUtcNow(Minute.AddSeconds(125));
        _minutes.TakeClosed().ShouldBeEmpty();
    }

    // Decision 12: the current minute of trades goes to the board of forming values.
    [Test]
    public void Trade_gives_its_minute_so_far()
    {
        _minutes.Add(Btc, new TradeEvent(Minute.AddSeconds(1), TradeSide.Buy, Quantity: 2m, Price: 150m));

        _minutes.Add(Btc, new TradeEvent(Minute.AddSeconds(2), TradeSide.Sell, Quantity: 3m, Price: 149m))
            .ShouldBe(new TradeFlowMinute(Minute, BuyVolume: 2m, SellVolume: 3m, BuyTurnover: 300m, SellTurnover: 447m, BuyTrades: 1, SellTrades: 1));
    }
}