using System.Globalization;

using Microsoft.Extensions.Time.Testing;

using NSubstitute;

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

/// <summary>Decisions 3 and 6, task 5.3 and 5.5: the live lane writes what the streams bring, with the coverage it gives and the breaks of the streams.</summary>
public class LiveWriterServiceTests
{
    private static readonly DateTimeOffset Minute = DateTimeOffset.Parse("2026-11-11T12:04:00Z", CultureInfo.InvariantCulture);
    private static readonly Instrument Btc = new Instrument(MarketCategory.Linear, "BTCUSDT");
    private static readonly Instrument Sol = new Instrument(MarketCategory.Linear, "SOLUSDT");

    private FakeTimeProvider _time = null!;
    private IMarketStore _store = null!;
    private ICollectorJournal _journal = null!;
    private FormingBoardService _board = null!;
    private List<LiveBatch> _written = null!;
    private Exception? _failure;
    private LiveWriterService _writer = null!;

    [SetUp]
    public void SetUp()
    {
        _time = new FakeTimeProvider(Minute);
        _store = Substitute.For<IMarketStore>();
        _journal = Substitute.For<ICollectorJournal>();
        _board = new FormingBoardService();
        _written = [];
        _failure = null;

        // Every batch is kept, also the one that fails: the next write of the store succeeds.
        _store.WriteLiveAsync(default!, default).ReturnsForAnyArgs(call =>
        {
            _written.Add(call.Arg<LiveBatch>()!);
            Exception? failure = _failure;
            _failure = null;

            return failure is null ? Task.CompletedTask : Task.FromException(failure);
        });
        _writer = new LiveWriterService(_store, new TradeMinutesService(_time), _board, _journal, _time);
    }

    // Scenarios «Минута закрылась» and «Минута без сделок в потоке»: a closed candle covers its minute.
    [Test]
    public async Task Closed_candle_goes_with_the_coverage_of_its_minute()
    {
        Candle candle = CandleGenerator.Generate(Minute);
        _writer.Closed(Btc, candle);

        await _writer.FlushAsync(withLiquidations: false, CancellationToken.None);

        _written.Single().Candles.ShouldBe([(Btc, candle)]);
        _written.Single().Coverage.ShouldBe([(SeriesKey.Candles(Btc, Interval.OneMinute), new TimeRange(Minute, Minute.AddMinutes(1)))]);
    }

    // Scenario «Формирующаяся свеча не хранится».
    [Test]
    public async Task Forming_candle_goes_to_the_board_only()
    {
        Candle forming = CandleGenerator.Generate(Minute.AddMinutes(1));

        _writer.Forming(Btc, forming);
        await _writer.FlushAsync(withLiquidations: true, CancellationToken.None);

        _board.Get([Btc]).Single().Candle.ShouldBe(forming);
        await _store.DidNotReceiveWithAnyArgs().WriteLiveAsync(default!, default);
    }

    // Scenario «Сделки одной минуты»: the minute goes out 5 seconds after its end, its current value is on the board meanwhile.
    [Test]
    public async Task Trades_of_a_minute_add_up_and_go_out_after_its_end()
    {
        _time.SetUtcNow(Minute.AddSeconds(30));
        _writer.Traded(Sol, new TradeEvent(Minute.AddSeconds(1), TradeSide.Buy, Quantity: 2m, Price: 150m));
        _writer.Traded(Sol, new TradeEvent(Minute.AddSeconds(2), TradeSide.Buy, Quantity: 1m, Price: 151m));
        _writer.Traded(Sol, new TradeEvent(Minute.AddSeconds(3), TradeSide.Sell, Quantity: 3m, Price: 149m));
        var expected = new TradeFlowMinute(Minute, BuyVolume: 3m, SellVolume: 3m, BuyTurnover: 451m, SellTurnover: 447m, BuyTrades: 2, SellTrades: 1);

        await _writer.FlushAsync(withLiquidations: false, CancellationToken.None);
        _board.Get([Sol]).Single().Trades.ShouldBe(expected);
        _written.ShouldBeEmpty();

        _time.SetUtcNow(Minute.AddSeconds(65));
        await _writer.FlushAsync(withLiquidations: false, CancellationToken.None);

        _written.Single().TradeMinutes.ShouldBe([(Sol, expected)]);
        _written.Single().Coverage.ShouldBeEmpty();
    }

    [Test]
    public async Task Trade_of_a_minute_that_went_out_does_not_reach_the_board()
    {
        _time.SetUtcNow(Minute.AddSeconds(65));
        await _writer.FlushAsync(withLiquidations: false, CancellationToken.None);

        _writer.Traded(Sol, TradeEventGenerator.Generate(Minute.AddSeconds(59)));

        _board.Get([Sol]).ShouldBeEmpty();
    }

    // Scenario «Ликвидация длинной позиции»: liquidations go in the flushes with liquidations, every 5 seconds.
    [Test]
    public async Task Liquidation_waits_for_a_flush_with_liquidations()
    {
        LiquidationEvent liquidation = new LiquidationEvent(Minute.AddSeconds(31.25), LiquidationSide.Long, Price: 90_000m, Quantity: 0.5m);
        _writer.Liquidated(Btc, liquidation);

        await _writer.FlushAsync(withLiquidations: false, CancellationToken.None);
        _written.ShouldBeEmpty();

        await _writer.FlushAsync(withLiquidations: true, CancellationToken.None);
        _written.Single().Liquidations.ShouldBe([(Btc, liquidation)]);
    }

    // Decision 8: liquidations are covered by the time their stream was connected, from the subscription on.
    [Test]
    public async Task Liquidations_of_the_pairs_are_covered_from_the_subscription_to_each_flush()
    {
        await _writer.StartAsync([Btc, Sol], subscribedAt: Minute, CancellationToken.None);

        _time.SetUtcNow(Minute.AddSeconds(5));
        await _writer.FlushAsync(withLiquidations: true, CancellationToken.None);
        _time.SetUtcNow(Minute.AddSeconds(10));
        await _writer.FlushAsync(withLiquidations: true, CancellationToken.None);

        _written[0].Coverage.ShouldBe([(Liquidations(Btc), Seconds(0, 5)), (Liquidations(Sol), Seconds(0, 5))]);
        _written[1].Coverage.ShouldBe([(Liquidations(Btc), Seconds(5, 10)), (Liquidations(Sol), Seconds(5, 10))]);
    }

    [Test]
    public async Task Flush_at_the_moment_of_the_subscription_covers_nothing()
    {
        _time.SetUtcNow(Minute);
        await _writer.StartAsync([Btc], subscribedAt: Minute, CancellationToken.None);

        await _writer.FlushAsync(withLiquidations: true, CancellationToken.None);

        _written.ShouldBeEmpty();
    }

    // Requirement «Покрытие»: a period the break touched is not covered.
    [Test]
    public async Task Break_of_the_liquidation_stream_cuts_its_coverage()
    {
        await _writer.StartAsync([Btc], subscribedAt: Minute, CancellationToken.None);

        _writer.Lost(LiveStream.Liquidations, Minute.AddSeconds(2));
        _time.SetUtcNow(Minute.AddSeconds(5));
        await _writer.FlushAsync(withLiquidations: true, CancellationToken.None);
        _writer.Restored(LiveStream.Liquidations, Minute.AddSeconds(7));
        _time.SetUtcNow(Minute.AddSeconds(10));
        await _writer.FlushAsync(withLiquidations: true, CancellationToken.None);

        _written[0].Coverage.ShouldBe([(Liquidations(Btc), Seconds(0, 2))]);
        _written[1].Coverage.ShouldBe([(Liquidations(Btc), Seconds(7, 10))]);
    }

    [Test]
    public async Task While_the_liquidation_stream_is_lost_nothing_is_covered()
    {
        await _writer.StartAsync([Btc], subscribedAt: Minute, CancellationToken.None);
        _writer.Lost(LiveStream.Liquidations, Minute.AddSeconds(2));
        _time.SetUtcNow(Minute.AddSeconds(5));
        await _writer.FlushAsync(withLiquidations: true, CancellationToken.None);

        _time.SetUtcNow(Minute.AddSeconds(10));
        await _writer.FlushAsync(withLiquidations: true, CancellationToken.None);

        _written.Single().Coverage.ShouldBe([(Liquidations(Btc), Seconds(0, 2))]);
    }

    // Scenario «Соединение восстановилось».
    [Test]
    public async Task Restored_stream_gives_its_break()
    {
        _writer.Lost(LiveStream.Trades, Minute.AddSeconds(5));
        _writer.Restored(LiveStream.Trades, Minute.AddSeconds(41));

        await _writer.FlushAsync(withLiquidations: false, CancellationToken.None);

        _written.Single().Breaks.ShouldBe([new StreamBreak(LiveStream.Trades, Minute.AddSeconds(5), Minute.AddSeconds(41))]);
    }

    // A resubscription loses the stream once more before it is back: the break starts at the first loss.
    [Test]
    public async Task Break_starts_at_the_first_loss()
    {
        _writer.Lost(LiveStream.Candles, Minute.AddSeconds(5));
        _writer.Lost(LiveStream.Candles, Minute.AddSeconds(9));
        _writer.Restored(LiveStream.Candles, Minute.AddSeconds(41));

        await _writer.FlushAsync(withLiquidations: false, CancellationToken.None);

        _written.Single().Breaks.ShouldBe([new StreamBreak(LiveStream.Candles, Minute.AddSeconds(5), Minute.AddSeconds(41))]);
    }

    [Test]
    public async Task Restored_without_a_loss_gives_no_break()
    {
        _writer.Restored(LiveStream.Candles, Minute.AddSeconds(41));

        await _writer.FlushAsync(withLiquidations: false, CancellationToken.None);

        _written.ShouldBeEmpty();
    }

    // Scenario «Перезапуск сервиса»: the streams were broken from the end of the last written minute to the subscription.
    [Test]
    public async Task Start_after_a_stop_gives_the_breaks_of_every_stream()
    {
        DateTimeOffset written = DateTimeOffset.Parse("2026-11-11T14:02:00Z", CultureInfo.InvariantCulture);
        DateTimeOffset subscribed = DateTimeOffset.Parse("2026-11-11T14:05:10Z", CultureInfo.InvariantCulture);
        _store.GetLastMinuteEndAsync(Arg.Any<CancellationToken>()).Returns(written);

        await _writer.StartAsync([], subscribed, CancellationToken.None);
        await _writer.FlushAsync(withLiquidations: false, CancellationToken.None);

        _written.Single().Breaks.ShouldBe(
        [
            new StreamBreak(LiveStream.Candles, written, subscribed),
            new StreamBreak(LiveStream.Trades, written, subscribed),
            new StreamBreak(LiveStream.Liquidations, written, subscribed)
        ]);
    }

    [Test]
    public async Task First_start_gives_no_break()
    {
        _store.GetLastMinuteEndAsync(Arg.Any<CancellationToken>()).Returns((DateTimeOffset?)null);

        await _writer.StartAsync([], Minute, CancellationToken.None);
        await _writer.FlushAsync(withLiquidations: false, CancellationToken.None);

        _written.ShouldBeEmpty();
    }

    // Nothing the streams brought is lost while the database is out: the failed batch goes again with the next one.
    [Test]
    public async Task Failed_batch_goes_again_with_the_next_one()
    {
        Candle first = CandleGenerator.Generate(Minute);
        Candle second = CandleGenerator.Generate(Minute.AddMinutes(1));
        var failure = new InvalidOperationException("database is out");
        _failure = failure;
        _writer.Closed(Btc, first);
        await _writer.FlushAsync(withLiquidations: false, CancellationToken.None);

        _writer.Closed(Btc, second);
        await _writer.FlushAsync(withLiquidations: false, CancellationToken.None);

        _journal.Received(1).Failed("live", failure);
        _written[1].Candles.ShouldBe([(Btc, first), (Btc, second)]);
    }

    // A write cut by the stop of the service is no failure of the store.
    [Test]
    public async Task Cancelled_write_is_not_a_failure()
    {
        using var stop = new CancellationTokenSource();
        await stop.CancelAsync();
        _failure = new OperationCanceledException(stop.Token);
        _writer.Closed(Btc, CandleGenerator.Generate(Minute));

        await Should.ThrowAsync<OperationCanceledException>(() => _writer.FlushAsync(withLiquidations: false, stop.Token));

        _journal.DidNotReceiveWithAnyArgs().Failed(default!, default!);
    }

    // The loop flushes every second and takes the liquidations every fifth second.
    [Test]
    public async Task Run_flushes_every_second_and_liquidations_every_five()
    {
        using var stop = new CancellationTokenSource();
        Task run = _writer.RunAsync(stop.Token);
        _writer.Closed(Btc, CandleGenerator.Generate(Minute));
        _writer.Liquidated(Btc, LiquidationEventGenerator.Generate(Minute.AddSeconds(30)));

        _time.Advance(TimeSpan.FromSeconds(1));
        await WaitForWritesAsync(1);
        _written[0].Liquidations.ShouldBeEmpty();

        for (var second = 2; second <= 5; second++)
        {
            _time.Advance(TimeSpan.FromSeconds(1));
        }

        await WaitForWritesAsync(2);
        _written[1].Liquidations.Count.ShouldBe(1);

        await stop.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(() => run);
    }

    private static SeriesKey Liquidations(Instrument instrument)
    {
        return SeriesKey.Of(DataKind.Liquidations, instrument);
    }

    private static TimeRange Seconds(int from, int to)
    {
        return new TimeRange(Minute.AddSeconds(from), Minute.AddSeconds(to));
    }

    private async Task WaitForWritesAsync(int count)
    {
        DateTime until = DateTime.UtcNow + TimeSpan.FromSeconds(10);

        while (_written.Count < count && DateTime.UtcNow < until)
        {
            await Task.Delay(10);
        }

        _written.Count.ShouldBe(count);
    }
}
