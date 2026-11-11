using System.Globalization;

using MediatR;

using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitFox.MarketData.Application.Commands.History.TopUpFunding;
using ProfitFox.MarketData.Application.Commands.Pairs.RefreshPairs;
using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Application.Ports;
using ProfitFox.MarketData.Application.Services.Collection;
using ProfitFox.MarketData.Application.Services.Forming;
using ProfitFox.MarketData.Domain.Entities;
using ProfitFox.MarketData.Domain.Entities.Enums;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entity;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObject;

using Shouldly;

namespace ProfitFox.MarketData.Tests.Unit.Application.Services.Collection;

/// <summary>Decisions 4 and 5, tasks 5.2 and 5.4: the collector keeps the subscriptions on the list of pairs and reads open interest and funding.</summary>
public class CollectorSessionServiceTests
{
    private static readonly DateTimeOffset Minute = DateTimeOffset.Parse("2026-11-11T12:04:00Z", CultureInfo.InvariantCulture);
    private static readonly MarketPair Btc = new MarketPair(new Instrument(MarketCategory.Linear, "BTCUSDT"), Listed: true, ListedAt: null);
    private static readonly MarketPair Sol = new MarketPair(new Instrument(MarketCategory.Linear, "SOLUSDT"), Listed: true, ListedAt: null);
    private static readonly MarketPair Eth = new MarketPair(new Instrument(MarketCategory.Spot, "ETHUSDT"), Listed: true, ListedAt: null);

    private FakeTimeProvider _time = null!;
    private IMediator _mediator = null!;
    private IMarketStore _store = null!;
    private IMarketDataStreams _streams = null!;
    private IExchangeMarketData _exchange = null!;
    private ICollectorJournal _journal = null!;
    private FormingBoardService _board = null!;
    private LiveWriterService _writer = null!;
    private List<LiveBatch> _written = null!;
    private List<Subscription> _subscriptions = null!;
    private IReadOnlyList<MarketPair> _listed = null!;
    private LiveStream? _refused;
    private int _refreshFailures;
    private CollectorSessionService _session = null!;

    [SetUp]
    public void SetUp()
    {
        _time = new FakeTimeProvider(Minute);
        _mediator = Substitute.For<IMediator>();
        _store = Substitute.For<IMarketStore>();
        _streams = Substitute.For<IMarketDataStreams>();
        _exchange = Substitute.For<IExchangeMarketData>();
        _journal = Substitute.For<ICollectorJournal>();
        _board = new FormingBoardService();
        _written = [];
        _subscriptions = [];
        _listed = [Btc, Eth];
        _refused = null;

        _refreshFailures = 0;
        _mediator.Send(Arg.Any<RefreshPairsCommand>(), Arg.Any<CancellationToken>())
            .Returns(_ => _refreshFailures-- > 0 ? throw new ExchangeUnavailableException("no answer") : _listed);
        _store.WriteLiveAsync(default!, default).ReturnsForAnyArgs(call =>
        {
            _written.Add(call.Arg<LiveBatch>()!);

            return Task.CompletedTask;
        });
        _streams.SubscribeMinuteCandlesAsync(default!, default!, default!, default!, default!)
            .ReturnsForAnyArgs(call => Subscribe(LiveStream.Candles, call.ArgAt<IReadOnlyList<Instrument>>(0), call.Args()));
        _streams.SubscribeTradesAsync(default!, default!, default!, default!)
            .ReturnsForAnyArgs(call => Subscribe(LiveStream.Trades, call.ArgAt<IReadOnlyList<Instrument>>(0), call.Args()));
        _streams.SubscribeLiquidationsAsync(default!, default!, default!, default!)
            .ReturnsForAnyArgs(call => Subscribe(LiveStream.Liquidations, call.ArgAt<IReadOnlyList<Instrument>>(0), call.Args()));

        _writer = new LiveWriterService(_store, new TradeMinutesService(_time), _board, _journal, _time);
        _session = new CollectorSessionService(_mediator, _streams, _writer, _exchange, _journal, new CollectorSettings { Symbols = ["BTCUSDT"] }, _time);
    }

    // Decision 4: the list narrowed by the settings, as the scanner narrows it in tests.
    [Test]
    public async Task List_of_pairs_is_narrowed_by_the_symbols_of_the_settings()
    {
        await _session.RefreshAsync(CancellationToken.None);

        await _mediator.Received(1).Send(Arg.Is<RefreshPairsCommand>(command => command!.Symbols.SequenceEqual(new[] { "BTCUSDT" })), Arg.Any<CancellationToken>());
    }

    // Decision 5: candles and trades of every pair, liquidations of the linear ones.
    [Test]
    public async Task First_refresh_subscribes_every_stream_to_its_pairs()
    {
        await _session.RefreshAsync(CancellationToken.None);

        Pairs(LiveStream.Candles).ShouldBe([Btc.Instrument, Eth.Instrument]);
        Pairs(LiveStream.Trades).ShouldBe([Btc.Instrument, Eth.Instrument]);
        Pairs(LiveStream.Liquidations).ShouldBe([Btc.Instrument]);
    }

    // The live lane starts at the subscription: the liquidations of the linear pairs are covered from then.
    [Test]
    public async Task First_refresh_starts_the_live_lane_at_the_subscription()
    {
        await _session.RefreshAsync(CancellationToken.None);

        _time.Advance(TimeSpan.FromSeconds(5));
        await _writer.FlushAsync(withLiquidations: true, CancellationToken.None);

        _written.Single().Coverage.ShouldBe([(SeriesKey.Of(DataKind.Liquidations, Btc.Instrument), new TimeRange(Minute, Minute.AddSeconds(5)))]);
    }

    [Test]
    public async Task Same_list_keeps_the_subscriptions()
    {
        await _session.RefreshAsync(CancellationToken.None);

        await _session.RefreshAsync(CancellationToken.None);

        _subscriptions.Count.ShouldBe(3);
        _subscriptions.ShouldAllBe(subscription => !subscription.Disposed);
    }

    // Scenario «Новая пара»: the streams are subscribed again with it, the time in between is a break of every stream.
    [Test]
    public async Task New_pair_subscribes_the_streams_again_with_it()
    {
        await _session.RefreshAsync(CancellationToken.None);
        _listed = [Btc, Eth, Sol];

        await _session.RefreshAsync(CancellationToken.None);

        _subscriptions.Take(3).ShouldAllBe(subscription => subscription.Disposed);
        Pairs(LiveStream.Candles).ShouldBe([Btc.Instrument, Eth.Instrument, Sol.Instrument]);
        Pairs(LiveStream.Liquidations).ShouldBe([Btc.Instrument, Sol.Instrument]);
        await _writer.FlushAsync(withLiquidations: false, CancellationToken.None);
        _written.Single().Breaks.ShouldBe(
            [new StreamBreak(LiveStream.Candles, Minute, Minute), new StreamBreak(LiveStream.Trades, Minute, Minute), new StreamBreak(LiveStream.Liquidations, Minute, Minute)],
            ignoreOrder: true);
    }

    // The liquidations of the new pair are covered from its subscription on.
    [Test]
    public async Task New_linear_pair_has_its_liquidations_covered_from_its_subscription()
    {
        await _session.RefreshAsync(CancellationToken.None);
        _time.Advance(TimeSpan.FromSeconds(3));
        _listed = [Btc, Eth, Sol];

        await _session.RefreshAsync(CancellationToken.None);
        _time.Advance(TimeSpan.FromSeconds(2));
        await _writer.FlushAsync(withLiquidations: true, CancellationToken.None);

        _written.Single().Coverage.ShouldBe(
        [
            (SeriesKey.Of(DataKind.Liquidations, Btc.Instrument), new TimeRange(Minute, Minute.AddSeconds(3))),
            (SeriesKey.Of(DataKind.Liquidations, Btc.Instrument), new TimeRange(Minute.AddSeconds(3), Minute.AddSeconds(5))),
            (SeriesKey.Of(DataKind.Liquidations, Sol.Instrument), new TimeRange(Minute.AddSeconds(3), Minute.AddSeconds(5)))
        ]);
    }

    // Scenario «Пара снята с торгов»: the streams go on without it.
    [Test]
    public async Task Pair_out_of_the_list_is_no_longer_subscribed()
    {
        await _session.RefreshAsync(CancellationToken.None);
        _listed = [Eth];

        await _session.RefreshAsync(CancellationToken.None);

        Pairs(LiveStream.Candles).ShouldBe([Eth.Instrument]);
        Pairs(LiveStream.Liquidations).ShouldBeEmpty();
    }

    // A refused subscription leaves none: the next refresh subscribes again, also on the same list.
    [Test]
    public async Task Refused_subscription_leaves_none_and_the_next_refresh_subscribes_again()
    {
        _refused = LiveStream.Liquidations;

        await Should.ThrowAsync<ExchangeUnavailableException>(() => _session.RefreshAsync(CancellationToken.None));
        _subscriptions.ShouldAllBe(subscription => subscription.Disposed);

        _refused = null;
        await _session.RefreshAsync(CancellationToken.None);
        _subscriptions.Count(subscription => !subscription.Disposed).ShouldBe(3);
    }

    // The live lane did not start: no subscription is left, so the next refresh subscribes and starts it again.
    [Test]
    public async Task Failed_start_of_the_live_lane_leaves_no_subscription()
    {
        _store.GetLastMinuteEndAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException<DateTimeOffset?>(new TimeoutException("no database")), Task.FromResult<DateTimeOffset?>(null));

        await Should.ThrowAsync<TimeoutException>(() => _session.RefreshAsync(CancellationToken.None));
        _subscriptions.ShouldAllBe(subscription => subscription.Disposed);

        await _session.RefreshAsync(CancellationToken.None);
        _subscriptions.Count(subscription => !subscription.Disposed).ShouldBe(3);
        await _store.Received(2).GetLastMinuteEndAsync(Arg.Any<CancellationToken>());
    }

    // Decision 5: a closed candle goes to the live lane, a forming one to the board.
    [Test]
    public async Task Candles_of_the_stream_go_to_the_live_lane_and_the_board()
    {
        await _session.RefreshAsync(CancellationToken.None);
        Subscription candles = Last(LiveStream.Candles);
        Candle closed = CandleGenerator.Generate(Minute.AddMinutes(-1));
        Candle forming = CandleGenerator.Generate(Minute);

        candles.Callback<Action<Instrument, Candle>>(1)(Btc.Instrument, forming);
        candles.Callback<Action<Instrument, Candle>>(2)(Btc.Instrument, closed);
        Lost(candles, Minute.AddSeconds(1));
        Restored(candles, Minute.AddSeconds(2));
        await _writer.FlushAsync(withLiquidations: false, CancellationToken.None);

        _board.Get([Btc.Instrument]).Single().Candle.ShouldBe(forming);
        _written.Single().Candles.ShouldBe([(Btc.Instrument, closed)]);
        _written.Single().Breaks.ShouldBe([new StreamBreak(LiveStream.Candles, Minute.AddSeconds(1), Minute.AddSeconds(2))]);
    }

    [Test]
    public async Task Trades_of_the_stream_go_to_the_minutes_of_trades()
    {
        await _session.RefreshAsync(CancellationToken.None);
        Subscription trades = Last(LiveStream.Trades);

        trades.Callback<Action<Instrument, TradeEvent>>(1)(Btc.Instrument, new TradeEvent(Minute.AddSeconds(1), TradeSide.Buy, Quantity: 2m, Price: 150m));
        Lost(trades, Minute.AddSeconds(1));
        Restored(trades, Minute.AddSeconds(2));
        await _writer.FlushAsync(withLiquidations: false, CancellationToken.None);

        _board.Get([Btc.Instrument]).Single().Trades!.BuyVolume.ShouldBe(2m);
        _written.Single().Breaks.ShouldBe([new StreamBreak(LiveStream.Trades, Minute.AddSeconds(1), Minute.AddSeconds(2))]);
    }

    [Test]
    public async Task Liquidations_of_the_stream_go_to_the_live_lane()
    {
        await _session.RefreshAsync(CancellationToken.None);
        Subscription liquidations = Last(LiveStream.Liquidations);
        LiquidationEvent liquidation = LiquidationEventGenerator.Generate(Minute);

        liquidations.Callback<Action<Instrument, LiquidationEvent>>(1)(Btc.Instrument, liquidation);
        Lost(liquidations, Minute);
        Restored(liquidations, Minute.AddSeconds(2));
        await _writer.FlushAsync(withLiquidations: true, CancellationToken.None);

        _written.Single().Liquidations.ShouldBe([(Btc.Instrument, liquidation)]);
        _written.Single().Breaks.ShouldBe([new StreamBreak(LiveStream.Liquidations, Minute, Minute.AddSeconds(2))]);
    }

    // Scenario «Точка открытого интереса»: a read of the tickers in the first minute of a step gives the points of its start, of the listed pairs only.
    [Test]
    public async Task Tickers_in_the_first_minute_of_a_step_give_the_open_interest_of_its_start()
    {
        _exchange.GetOpenInterestNowAsync(Arg.Any<CancellationToken>()).Returns([(Btc.Instrument, 51234.567m), (Sol.Instrument, 8000000m)]);
        await _session.RefreshAsync(CancellationToken.None);
        _time.SetUtcNow(Minute.AddMinutes(1).AddSeconds(59));

        await _session.ReadOpenInterestAsync(CancellationToken.None);
        await _writer.FlushAsync(withLiquidations: false, CancellationToken.None);

        _written.Single().OpenInterest.ShouldBe([(Btc.Instrument, new OpenInterestPoint(Minute.AddMinutes(1), Value: 51234.567m))]);
    }

    // Only a read in the first minute tells the open interest of the start of the step; a later step is left to the history.
    [Test]
    public async Task After_the_first_minute_of_a_step_the_tickers_are_not_read()
    {
        await _session.RefreshAsync(CancellationToken.None);
        _time.SetUtcNow(Minute.AddMinutes(2));

        await _session.ReadOpenInterestAsync(CancellationToken.None);

        await _exchange.DidNotReceiveWithAnyArgs().GetOpenInterestNowAsync(default);
    }

    // The collector starts once it has the list of pairs and its subscriptions; until then it tries again every 30 seconds.
    [Test]
    public async Task Start_without_the_list_of_pairs_is_tried_again_after_a_while()
    {
        _refreshFailures = 1;
        using var stop = new CancellationTokenSource();

        Task run = _session.RunAsync(stop.Token);
        _journal.Received(1).Failed("pairs", Arg.Any<ExchangeUnavailableException>());
        _time.Advance(CollectorSessionService.Retry - TimeSpan.FromSeconds(1));
        await Task.Delay(50);
        _subscriptions.ShouldBeEmpty();
        _time.Advance(TimeSpan.FromSeconds(1));

        await WaitUntilAsync(() => _subscriptions.Count == 3);
        await StopAsync(stop, run);
    }

    // Requirement «Пары сервиса»: the list is read again every 10 minutes.
    [Test]
    public async Task List_of_pairs_is_read_again_every_ten_minutes()
    {
        using var stop = new CancellationTokenSource();

        Task run = _session.RunAsync(stop.Token);
        _time.Advance(CollectorSessionService.PairsEvery - TimeSpan.FromSeconds(1));
        await Task.Delay(50);
        Refreshes().ShouldBe(1);
        _listed = [Btc, Eth, Sol];
        _time.Advance(TimeSpan.FromSeconds(1));

        await WaitUntilAsync(() => Refreshes() == 2 && _subscriptions.Count == 6);
        await StopAsync(stop, run);
    }

    // Scenario «Списание фандинга»: the funding of the listed pairs is topped up at the start and every 10 minutes; a failed top-up waits for the next one.
    [Test]
    public async Task Funding_is_topped_up_at_the_start_and_every_ten_minutes()
    {
        _mediator.Send(Arg.Any<TopUpFundingCommand>(), Arg.Any<CancellationToken>()).Returns(Task.FromException(new ExchangeUnavailableException("no answer")), Task.CompletedTask);
        using var stop = new CancellationTokenSource();

        Task run = _session.RunAsync(stop.Token);
        await _mediator.Received(1).Send(Arg.Is<TopUpFundingCommand>(command => command!.Pairs.SequenceEqual(new[] { Btc, Eth })), Arg.Any<CancellationToken>());
        _journal.Received(1).Failed("funding", Arg.Any<ExchangeUnavailableException>());
        _time.Advance(CollectorSessionService.FundingEvery);

        await WaitUntilAsync(() => TopUps() == 2);
        await StopAsync(stop, run);
    }

    // Scenario «Точка открытого интереса»: the tickers are read at the start of every step.
    [Test]
    public async Task Tickers_are_read_at_the_start_of_every_step()
    {
        _exchange.GetOpenInterestNowAsync(Arg.Any<CancellationToken>()).Returns([]);
        using var stop = new CancellationTokenSource();

        Task run = _session.RunAsync(stop.Token);
        _time.Advance(TimeSpan.FromSeconds(59));
        await Task.Delay(50);
        await _exchange.DidNotReceiveWithAnyArgs().GetOpenInterestNowAsync(default);
        _time.Advance(TimeSpan.FromSeconds(1));
        await WaitUntilAsync(() => _exchange.ReceivedCalls().Count() == 1);
        _time.Advance(TimeSpan.FromMinutes(5));

        await WaitUntilAsync(() => _exchange.ReceivedCalls().Count() == 2);
        await StopAsync(stop, run);
    }

    // A failed read of the tickers is tried again while the first minute of the step lasts, so the point is there before 12:07.
    [Test]
    public async Task Failed_read_of_the_tickers_is_tried_again_within_the_minute()
    {
        _time.SetUtcNow(Minute.AddMinutes(1));
        _exchange.GetOpenInterestNowAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<(Instrument, decimal)>>(new ExchangeUnavailableException("no answer")), Task.FromResult<IReadOnlyList<(Instrument, decimal)>>([]));
        using var stop = new CancellationTokenSource();

        Task run = _session.RunAsync(stop.Token);
        _journal.Received(1).Failed("open-interest", Arg.Any<ExchangeUnavailableException>());
        _time.Advance(CollectorSessionService.OpenInterestRetry);

        await WaitUntilAsync(() => _exchange.ReceivedCalls().Count() == 2);
        await StopAsync(stop, run);
    }

    // Decision 6: the live lane runs with the collector.
    [Test]
    public async Task Live_lane_writes_while_the_collector_runs()
    {
        using var stop = new CancellationTokenSource();

        Task run = _session.RunAsync(stop.Token);
        Last(LiveStream.Candles).Callback<Action<Instrument, Candle>>(2)(Btc.Instrument, CandleGenerator.Generate(Minute.AddMinutes(-1)));
        _time.Advance(LiveWriterService.Tick);

        await WaitUntilAsync(() => _written.Count == 1);
        await StopAsync(stop, run);
    }

    [Test]
    public async Task Stopped_collector_closes_its_subscriptions()
    {
        using var stop = new CancellationTokenSource();
        Task run = _session.RunAsync(stop.Token);

        await StopAsync(stop, run);

        _subscriptions.Count.ShouldBe(3);
        _subscriptions.ShouldAllBe(subscription => subscription.Disposed);
    }

    private static async Task StopAsync(CancellationTokenSource stop, Task run)
    {
        await stop.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(() => run);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        DateTime until = DateTime.UtcNow + TimeSpan.FromSeconds(10);

        while (!condition() && DateTime.UtcNow < until)
        {
            await Task.Delay(10);
        }

        condition().ShouldBeTrue();
    }

    private int Refreshes()
    {
        return _mediator.ReceivedCalls().Count(call => call.GetArguments()[0] is RefreshPairsCommand);
    }

    private int TopUps()
    {
        return _mediator.ReceivedCalls().Count(call => call.GetArguments()[0] is TopUpFundingCommand);
    }

    private static void Lost(Subscription subscription, DateTimeOffset at)
    {
        subscription.Callback<Action<DateTimeOffset>>(^2)(at);
    }

    private static void Restored(Subscription subscription, DateTimeOffset at)
    {
        subscription.Callback<Action<DateTimeOffset>>(^1)(at);
    }

    private Task<IAsyncDisposable> Subscribe(LiveStream stream, IReadOnlyList<Instrument> instruments, object?[] args)
    {
        if (stream == _refused)
        {
            return Task.FromException<IAsyncDisposable>(new ExchangeUnavailableException("refused"));
        }

        var subscription = new Subscription(stream, instruments, args);
        _subscriptions.Add(subscription);

        return Task.FromResult<IAsyncDisposable>(subscription);
    }

    private Subscription Last(LiveStream stream)
    {
        return _subscriptions.Last(subscription => subscription.Stream == stream);
    }

    private IReadOnlyList<Instrument> Pairs(LiveStream stream)
    {
        return Last(stream).Instruments;
    }

    private sealed class Subscription(LiveStream stream, IReadOnlyList<Instrument> instruments, object?[] args) : IAsyncDisposable
    {
        public LiveStream Stream { get; } = stream;

        public IReadOnlyList<Instrument> Instruments { get; } = instruments;

        public object?[] Args { get; } = args;

        public bool Disposed { get; private set; }

        public T Callback<T>(Index index)
        {
            return (T)Args[index]!;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;

            return ValueTask.CompletedTask;
        }
    }
}
