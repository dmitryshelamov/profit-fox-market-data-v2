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
    public void Updates_between_two_reads_come_as_the_last_one()
    {
        _board.Update(Btc, Candle(105m));
        _board.Update(Btc, Candle(106m));

        (IReadOnlyList<FormingValue> values, _) = _board.Changed([Btc], since: 0);

        values.Single().Candle.ShouldBe(Candle(106m));
    }

    // Scenario «Медленный клиент живой свечи»: a reader that comes back reads the last value.
    [Test]
    public void Read_after_the_last_version_gets_nothing_until_the_next_update()
    {
        _board.Update(Btc, Candle(105m));
        (_, long version) = _board.Changed([Btc], since: 0);

        _board.Changed([Btc], version).Values.ShouldBeEmpty();

        _board.Update(Btc, Candle(107m));
        _board.Changed([Btc], version).Values.Single().Candle.ShouldBe(Candle(107m));
    }

    [Test]
    public void Only_the_pairs_asked_for_are_read()
    {
        _board.Update(Btc, Candle(105m));
        _board.Update(Sol, Candle(150m));

        _board.Changed([Sol], since: 0).Values.Select(value => value.Instrument).ShouldBe([Sol]);
    }

    [Test]
    public void Pair_without_updates_is_not_read()
    {
        _board.Update(Btc, Candle(105m));

        _board.Changed([Btc, Sol], since: 0).Values.Select(value => value.Instrument).ShouldBe([Btc]);
    }

    [Test]
    public void Forming_candle_and_current_minute_of_trades_of_a_pair_come_together()
    {
        var trades = new TradeFlowMinute(Minute, 1m, 2m, 105m, 210m, 1, 1);
        _board.Update(Btc, Candle(105m));
        _board.Update(Btc, trades);

        FormingValue value = _board.Changed([Btc], since: 0).Values.Single();

        value.Candle.ShouldBe(Candle(105m));
        value.Trades.ShouldBe(trades);
    }

    private static Candle Candle(decimal close)
    {
        return new Candle(Minute, 100m, 110m, 90m, close, 3m, 315m);
    }
}
