using System.Collections.Concurrent;
using System.Globalization;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Domain.Entities;
using ProfitFox.MarketData.Domain.Entities.Enums;
using ProfitFox.MarketData.Domain.ValueObjects;
using ProfitFox.MarketData.Infrastructure.Exchanges.Bybit;
using ProfitFox.MarketData.Tests.Shared.Bybit;

using Shouldly;

namespace ProfitFox.MarketData.Tests.Integration.Exchanges.Bybit;

/// <summary>Decision 5: the minute candles, liquidations and trades of the whole market, each stream on connections of its own, against the fake Bybit.</summary>
public class BybitMarketDataStreamsTests
{
    private static readonly DateTimeOffset Open = DateTimeOffset.Parse("2026-11-11T12:04:00Z", CultureInfo.InvariantCulture);
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-11T12:05:20Z", CultureInfo.InvariantCulture);
    private static readonly Instrument Sol = new Instrument(MarketCategory.Linear, "SOLUSDT");
    private static readonly Instrument Btc = new Instrument(MarketCategory.Spot, "BTCUSDT");

    private FakeBybitSocketServer _server = null!;
    private BybitMarketDataStreams _streams = null!;

    [SetUp]
    public async Task SetUp()
    {
        _server = await FakeBybitSocketServer.StartAsync();
        _streams = new BybitMarketDataStreams(NullLoggerFactory.Instance, new BybitSettings(SocketAddress: _server.Address), new FakeTimeProvider(Now));
    }

    [TearDown]
    public async Task TearDown()
    {
        await _server.DisposeAsync();
    }

    // Bybit takes at most 10 topics in a subscribe request of spot; the service puts at most 100 topics on a connection.
    [Test]
    public async Task Candles_go_in_batches_within_the_limits_of_a_request_and_of_a_connection()
    {
        Instrument[] linear = [.. Enumerable.Range(0, 250).Select(i => new Instrument(MarketCategory.Linear, $"C{i:000}USDT"))];
        Instrument[] spot = [.. Enumerable.Range(0, 30).Select(i => new Instrument(MarketCategory.Spot, $"S{i:000}USDT"))];

        await using IAsyncDisposable subscription = await SubscribeCandlesAsync([.. linear, .. spot]);

        linear.ShouldAllBe(instrument => _server.Subscribed($"kline.1.{instrument.Symbol}"));
        spot.ShouldAllBe(instrument => _server.Subscribed($"kline.1.{instrument.Symbol}"));
        _server.SubscribeSizes.ShouldAllBe(size => size <= 10);
        _server.TopicsPerConnection("/v5/public/linear").ShouldAllBe(topics => topics <= 100);
        _server.Connections("/v5/public/linear").ShouldBe(3);
        _server.Connections("/v5/public/spot").ShouldBe(1);
    }

    // Decision 5: an update with confirm = false goes to the board of forming values, one with confirm = true to the writing.
    [Test]
    public async Task Forming_and_closed_candles_go_to_their_own_handlers()
    {
        var forming = new BlockingCollection<(Instrument, Candle)>();
        var closed = new BlockingCollection<(Instrument, Candle)>();
        await using IAsyncDisposable subscription = await _streams.SubscribeMinuteCandlesAsync(
            [Sol], (instrument, candle) => forming.Add((instrument, candle)), (instrument, candle) => closed.Add((instrument, candle)), _ => { }, _ => { });

        await _server.PushAsync("kline.1.SOLUSDT", [FakeBybitSocketServer.Kline(Open, 150m, confirm: false)]);
        await _server.PushAsync("kline.1.SOLUSDT", [FakeBybitSocketServer.Kline(Open, 151.5m, confirm: true, volume: 2m, turnover: 303m)]);

        forming.TryTake(out (Instrument Instrument, Candle Candle) current, TimeSpan.FromSeconds(10)).ShouldBeTrue();
        current.ShouldBe((Sol, new Candle(Open, 150m, 150m, 150m, 150m, 1m, 150m)));
        closed.TryTake(out (Instrument Instrument, Candle Candle) last, TimeSpan.FromSeconds(10)).ShouldBeTrue();
        last.ShouldBe((Sol, new Candle(Open, 151.5m, 151.5m, 151.5m, 151.5m, 2m, 303m)));
        forming.TryTake(out _, TimeSpan.FromMilliseconds(200)).ShouldBeFalse();
        closed.TryTake(out _, TimeSpan.FromMilliseconds(200)).ShouldBeFalse();
    }

    [Test]
    public async Task Candles_of_a_spot_pair_come_with_their_category()
    {
        var closed = new BlockingCollection<Instrument>();
        await using IAsyncDisposable subscription = await _streams.SubscribeMinuteCandlesAsync([Btc], (_, _) => { }, (instrument, _) => closed.Add(instrument), _ => { }, _ => { });

        await _server.PushAsync("kline.1.BTCUSDT", [FakeBybitSocketServer.Kline(Open, 90_000m, confirm: true)]);

        closed.TryTake(out Instrument? instrument, TimeSpan.FromSeconds(10)).ShouldBeTrue();
        instrument.ShouldBe(Btc);
    }

    // Requirement "Разрывы потоков сервиса": both ends of a break are told with the time of the service.
    [Test]
    public async Task Break_of_the_candle_stream_is_told_lost_then_restored()
    {
        var events = new BlockingCollection<(string, DateTimeOffset)>();
        await using IAsyncDisposable subscription = await _streams.SubscribeMinuteCandlesAsync(
            [Sol, Btc], (_, _) => { }, (_, _) => { }, at => events.Add(("lost", at)), at => events.Add(("restored", at)));

        await _server.DropAsync();

        events.TryTake(out (string, DateTimeOffset) first, TimeSpan.FromSeconds(30)).ShouldBeTrue();
        events.TryTake(out (string, DateTimeOffset) second, TimeSpan.FromSeconds(30)).ShouldBeTrue();
        first.ShouldBe(("lost", Now));
        second.ShouldBe(("restored", Now));
        events.TryTake(out _, TimeSpan.FromSeconds(2)).ShouldBeFalse();
    }

    // The spot batch goes first and is subscribed by the time the linear one is refused: nothing of the stream stays behind.
    [Test]
    public async Task Refused_candle_subscription_means_the_exchange_is_unavailable_and_takes_the_stream_off()
    {
        _server.RefusedTopics["kline.1.SOLUSDT"] = true;

        await Should.ThrowAsync<ExchangeUnavailableException>(() => SubscribeCandlesAsync([Btc, Sol]));

        await WaitForNoConnectionsAsync();
    }

    [Test]
    public async Task Disposing_takes_every_candle_subscription_off()
    {
        IAsyncDisposable subscription = await SubscribeCandlesAsync([Sol, Btc]);

        await subscription.DisposeAsync();

        await WaitForNoConnectionsAsync();
        _server.Subscribed("kline.1.SOLUSDT").ShouldBeFalse();
        _server.Subscribed("kline.1.BTCUSDT").ShouldBeFalse();
    }

    // Bybit's S is the side of the liquidated position: "a Buy update means that a long position has been liquidated".
    [TestCase("Buy", LiquidationSide.Long)]
    [TestCase("Sell", LiquidationSide.Short)]
    public async Task Liquidation_comes_with_its_instrument_time_and_the_side_of_the_position(string positionSide, LiquidationSide side)
    {
        var at = DateTimeOffset.Parse("2026-11-11T12:04:07.350Z", CultureInfo.InvariantCulture);
        var liquidations = new BlockingCollection<(Instrument, LiquidationEvent)>();
        await using IAsyncDisposable subscription = await _streams.SubscribeLiquidationsAsync(
            [Sol], (instrument, liquidation) => liquidations.Add((instrument, liquidation)), _ => { }, _ => { });

        await _server.PushAsync("allLiquidation.SOLUSDT", [FakeBybitSocketServer.Liquidation("SOLUSDT", positionSide, 150.2m, 12.5m, at)]);

        liquidations.TryTake(out (Instrument Instrument, LiquidationEvent Event) liquidated, TimeSpan.FromSeconds(10)).ShouldBeTrue();
        liquidated.ShouldBe((Sol, new LiquidationEvent(at, side, 150.2m, 12.5m)));
    }

    // Task 5.1: 25 pairs, 3 subscribe requests per topic.
    [Test]
    public async Task Liquidations_of_25_pairs_go_in_3_subscribe_requests()
    {
        Instrument[] linear = LinearPairs(25);

        await using IAsyncDisposable subscription = await _streams.SubscribeLiquidationsAsync(linear, (_, _) => { }, _ => { }, _ => { });

        linear.ShouldAllBe(instrument => _server.Subscribed($"allLiquidation.{instrument.Symbol}"));
        _server.SubscribeSizes.ShouldBe([10, 10, 5]);
    }

    [Test]
    public async Task Refused_liquidation_subscription_means_the_exchange_is_unavailable()
    {
        _server.RefusedTopics["allLiquidation.SOLUSDT"] = true;

        await Should.ThrowAsync<ExchangeUnavailableException>(() => _streams.SubscribeLiquidationsAsync([Sol], (_, _) => { }, _ => { }, _ => { }));
    }

    [Test]
    public async Task Break_of_the_liquidation_stream_is_told_lost_then_restored()
    {
        var events = new BlockingCollection<(string, DateTimeOffset)>();
        await using IAsyncDisposable subscription = await _streams.SubscribeLiquidationsAsync(
            [Sol], (_, _) => { }, at => events.Add(("lost", at)), at => events.Add(("restored", at)));

        await _server.DropAsync();

        events.TryTake(out (string, DateTimeOffset) first, TimeSpan.FromSeconds(30)).ShouldBeTrue();
        events.TryTake(out (string, DateTimeOffset) second, TimeSpan.FromSeconds(30)).ShouldBeTrue();
        first.ShouldBe(("lost", Now));
        second.ShouldBe(("restored", Now));
    }

    // A connection carries several subscriptions, and each tells its own break; the stream tells one break: lost with the first, back with the last.
    [Test]
    public async Task Break_of_several_subscriptions_is_told_once()
    {
        var lost = 0;
        var restored = new SemaphoreSlim(0);
        await using IAsyncDisposable subscription = await _streams.SubscribeLiquidationsAsync(
            LinearPairs(25), (_, _) => { }, _ => Interlocked.Increment(ref lost), _ => restored.Release());

        await _server.DropAsync();

        (await restored.WaitAsync(TimeSpan.FromSeconds(30))).ShouldBeTrue();
        (await restored.WaitAsync(TimeSpan.FromSeconds(2))).ShouldBeFalse();
        lost.ShouldBe(1);
    }

    [Test]
    public async Task Disposing_takes_every_liquidation_subscription_off()
    {
        IAsyncDisposable subscription = await _streams.SubscribeLiquidationsAsync([Sol], (_, _) => { }, _ => { }, _ => { });

        await subscription.DisposeAsync();

        await WaitForNoConnectionsAsync();
        _server.Subscribed("allLiquidation.SOLUSDT").ShouldBeFalse();
    }

    // Every trade comes with the side of its taker, a block trade and a trade of the RPI orders too, as in the daily archive.
    [TestCase(MarketCategory.Linear, "Buy", TradeSide.Buy, false, false)]
    [TestCase(MarketCategory.Spot, "Sell", TradeSide.Sell, false, false)]
    [TestCase(MarketCategory.Linear, "Sell", TradeSide.Sell, true, false)]
    [TestCase(MarketCategory.Spot, "Buy", TradeSide.Buy, false, true)]
    public async Task Trade_comes_with_its_instrument_time_side_volume_and_price(MarketCategory category, string takerSide, TradeSide side, bool block, bool rpi)
    {
        var at = DateTimeOffset.Parse("2026-11-11T12:04:59.999Z", CultureInfo.InvariantCulture);
        var instrument = new Instrument(category, "SOLUSDT");
        var trades = new BlockingCollection<(Instrument, TradeEvent)>();
        await using IAsyncDisposable subscription = await _streams.SubscribeTradesAsync([instrument], (traded, trade) => trades.Add((traded, trade)), _ => { }, _ => { });

        await _server.PushAsync("publicTrade.SOLUSDT", [FakeBybitSocketServer.Trade("SOLUSDT", takerSide, 2.5m, 150.25m, at, block, rpi)]);

        trades.TryTake(out (Instrument Instrument, TradeEvent Trade) received, TimeSpan.FromSeconds(10)).ShouldBeTrue();
        received.ShouldBe((instrument, new TradeEvent(at, side, 2.5m, 150.25m)));
    }

    // Task 5.1: 25 pairs, 3 subscribe requests per topic.
    [Test]
    public async Task Trades_of_25_pairs_go_in_3_subscribe_requests()
    {
        Instrument[] linear = LinearPairs(25);

        await using IAsyncDisposable subscription = await _streams.SubscribeTradesAsync(linear, (_, _) => { }, _ => { }, _ => { });

        linear.ShouldAllBe(instrument => _server.Subscribed($"publicTrade.{instrument.Symbol}"));
        _server.SubscribeSizes.ShouldBe([10, 10, 5]);
    }

    [Test]
    public async Task Trades_of_linear_and_spot_go_to_their_own_streams()
    {
        await using IAsyncDisposable subscription = await _streams.SubscribeTradesAsync([Sol, Btc], (_, _) => { }, _ => { }, _ => { });

        _server.Connections("/v5/public/linear").ShouldBe(1);
        _server.Connections("/v5/public/spot").ShouldBe(1);
    }

    [Test]
    public async Task Refused_trade_subscription_means_the_exchange_is_unavailable()
    {
        _server.RefusedTopics["publicTrade.SOLUSDT"] = true;

        await Should.ThrowAsync<ExchangeUnavailableException>(() => _streams.SubscribeTradesAsync([Sol], (_, _) => { }, _ => { }, _ => { }));
    }

    [Test]
    public async Task Break_of_the_trade_stream_is_told_lost_then_restored()
    {
        var events = new BlockingCollection<(string, DateTimeOffset)>();
        await using IAsyncDisposable subscription = await _streams.SubscribeTradesAsync(
            [Sol, Btc], (_, _) => { }, at => events.Add(("lost", at)), at => events.Add(("restored", at)));

        await _server.DropAsync();

        events.TryTake(out (string, DateTimeOffset) first, TimeSpan.FromSeconds(30)).ShouldBeTrue();
        events.TryTake(out (string, DateTimeOffset) second, TimeSpan.FromSeconds(30)).ShouldBeTrue();
        first.ShouldBe(("lost", Now));
        second.ShouldBe(("restored", Now));
        events.TryTake(out _, TimeSpan.FromSeconds(2)).ShouldBeFalse();
    }

    [Test]
    public async Task Disposing_takes_every_trade_subscription_off()
    {
        IAsyncDisposable subscription = await _streams.SubscribeTradesAsync([Sol, Btc], (_, _) => { }, _ => { }, _ => { });

        await subscription.DisposeAsync();

        await WaitForNoConnectionsAsync();
        _server.Subscribed("publicTrade.SOLUSDT").ShouldBeFalse();
        _server.Subscribed("publicTrade.BTCUSDT").ShouldBeFalse();
    }

    // Decision 5: the heavy stream of trades lives on connections of its own, apart from the candles.
    [Test]
    public async Task Candles_and_trades_of_a_pair_go_on_connections_of_their_own()
    {
        await using IAsyncDisposable candles = await SubscribeCandlesAsync([Sol]);
        await using IAsyncDisposable trades = await _streams.SubscribeTradesAsync([Sol], (_, _) => { }, _ => { }, _ => { });

        _server.TopicsPerConnection("/v5/public/linear").ShouldBe([1, 1], ignoreOrder: true);
    }

    private static Instrument[] LinearPairs(int count)
    {
        return [.. Enumerable.Range(0, count).Select(i => new Instrument(MarketCategory.Linear, $"C{i:000}USDT"))];
    }

    private Task<IAsyncDisposable> SubscribeCandlesAsync(IReadOnlyList<Instrument> instruments)
    {
        return _streams.SubscribeMinuteCandlesAsync(instruments, (_, _) => { }, (_, _) => { }, _ => { }, _ => { });
    }

    private async Task WaitForNoConnectionsAsync()
    {
        DateTime until = DateTime.UtcNow + TimeSpan.FromSeconds(10);

        while ((_server.Connections("/v5/public/linear") + _server.Connections("/v5/public/spot")) > 0 && DateTime.UtcNow < until)
        {
            await Task.Delay(20);
        }

        _server.Connections("/v5/public/linear").ShouldBe(0);
        _server.Connections("/v5/public/spot").ShouldBe(0);
    }
}
