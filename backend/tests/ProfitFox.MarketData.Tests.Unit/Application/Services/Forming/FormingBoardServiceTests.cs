using System.Globalization;

using ProfitFox.MarketData.Application.Services.Forming;
using ProfitFox.MarketData.Domain.Entities;
using ProfitFox.MarketData.Domain.Entities.Enums;
using ProfitFox.MarketData.Domain.ValueObjects;

using Shouldly;

namespace ProfitFox.MarketData.Tests.Unit.Application.Services.Forming;

/// <summary>Requirement «Формирующаяся свеча»: the last forming candle and the current minute of trades of each pair, in memory only.</summary>
public class FormingBoardServiceTests
{
    private static readonly DateTimeOffset Minute = DateTimeOffset.Parse("2026-11-09T12:04:00Z", CultureInfo.InvariantCulture);
    private static readonly Instrument Btc = new Instrument(MarketCategory.Linear, "BTCUSDT");
    private static readonly Instrument Sol = new Instrument(MarketCategory.Linear, "SOLUSDT");

    private FormingBoardService _board = null!;

    [SetUp]
    public void SetUp()
    {
        _board = new FormingBoardService();
    }

    // Scenario «Один открытый график»: of several updates between two reads only the last one comes.
    [Test]
    public void Update_replaces_the_last_value()
    {
        _board.Update(Btc, Candle(105m));
        _board.Update(Btc, Candle(106m));

        _board.Get([Btc]).Single().Candle.ShouldBe(Candle(106m));
    }

    [Test]
    public void Only_the_pairs_asked_for_are_read()
    {
        _board.Update(Btc, Candle(105m));
        _board.Update(Sol, Candle(150m));

        _board.Get([Sol]).Select(value => value.Instrument).ShouldBe([Sol]);
    }

    [Test]
    public void Pair_without_updates_is_not_read()
    {
        _board.Update(Btc, Candle(105m));

        _board.Get([Btc, Sol]).Select(value => value.Instrument).ShouldBe([Btc]);
    }

    [Test]
    public void Forming_candle_and_current_minute_of_trades_of_a_pair_come_together()
    {
        var trades = new TradeFlowMinute(Minute, BuyVolume: 1m, SellVolume: 2m, BuyTurnover: 105m, SellTurnover: 210m, BuyTrades: 1, SellTrades: 1);
        _board.Update(Btc, Candle(105m));
        _board.Update(Btc, trades);

        FormingValue value = _board.Get([Btc]).Single();

        value.Candle.ShouldBe(Candle(105m));
        value.Trades.ShouldBe(trades);
    }

    private static Candle Candle(decimal close)
    {
        return new Candle(Minute, Open: 100m, High: 110m, Low: 90m, close, Volume: 3m, Turnover: 315m);
    }
}