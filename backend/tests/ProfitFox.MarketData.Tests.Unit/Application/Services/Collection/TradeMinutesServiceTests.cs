using System.Globalization;

using Microsoft.Extensions.Time.Testing;

using ProfitFox.MarketData.Application.Services.Collection;
using ProfitFox.MarketData.Domain.Entities.Enums;
using ProfitFox.MarketData.Domain.ValueObjects;

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
        _minutes.Add(Btc, new TradeEvent(Minute.AddSeconds(1), TradeSide.Buy, 2m, 150m));
        _minutes.Add(Btc, new TradeEvent(Minute.AddSeconds(30), TradeSide.Sell, 3m, 149m));

        _time.SetUtcNow(Minute.AddSeconds(64));
        _minutes.TakeClosed().ShouldBeEmpty();

        _time.SetUtcNow(Minute.AddSeconds(65));
        _minutes.TakeClosed().ShouldBe([(Btc, new TradeFlowMinute(Minute, 2m, 3m, 300m, 447m, 1, 1))]);
    }

    [Test]
    public void Minutes_of_every_pair_go_out_together()
    {
        _minutes.Add(Btc, new TradeEvent(Minute.AddSeconds(1), TradeSide.Buy, 1m, 150m));
        _minutes.Add(Sol, new TradeEvent(Minute.AddSeconds(2), TradeSide.Buy, 1m, 20m));

        _time.SetUtcNow(Minute.AddSeconds(65));

        _minutes.TakeClosed().Select(entry => entry.Instrument).ShouldBe([Btc, Sol], ignoreOrder: true);
    }

    [Test]
    public void Minute_that_went_out_does_not_go_out_again()
    {
        _minutes.Add(Btc, new TradeEvent(Minute.AddSeconds(1), TradeSide.Buy, 1m, 150m));
        _time.SetUtcNow(Minute.AddSeconds(65));
        _minutes.TakeClosed();

        _minutes.TakeClosed().ShouldBeEmpty();
    }

    [Test]
    public void Minute_still_open_stays()
    {
        _minutes.Add(Btc, new TradeEvent(Minute.AddSeconds(1), TradeSide.Buy, 1m, 150m));
        _time.SetUtcNow(Minute.AddSeconds(65));
        _minutes.Add(Btc, new TradeEvent(Minute.AddSeconds(62), TradeSide.Sell, 1m, 151m));

        _minutes.TakeClosed().Single().Minute.OpenTime.ShouldBe(Minute);

        _time.SetUtcNow(Minute.AddSeconds(125));
        _minutes.TakeClosed().Single().Minute.OpenTime.ShouldBe(Minute.AddMinutes(1));
    }

    // The rebuild of the day from the archive brings such a trade back.
    [Test]
    public void Trade_of_a_minute_that_went_out_is_dropped()
    {
        _minutes.Add(Btc, new TradeEvent(Minute.AddSeconds(1), TradeSide.Buy, 1m, 150m));
        _time.SetUtcNow(Minute.AddSeconds(65));
        _minutes.TakeClosed();
        _minutes.Add(Btc, new TradeEvent(Minute.AddSeconds(59), TradeSide.Sell, 1m, 150m));

        _time.SetUtcNow(Minute.AddSeconds(125));

        _minutes.TakeClosed().ShouldBeEmpty();
    }
}
