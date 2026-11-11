using System.Collections.Concurrent;

using ProfitFox.MarketData.Application.Ports;
using ProfitFox.MarketData.Application.Services.Forming;
using ProfitFox.MarketData.Domain.Entities;
using ProfitFox.MarketData.Domain.Entities.Enums;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects;

namespace ProfitFox.MarketData.Application.Services.Collection;

/// <summary>
///     The live lane (decisions 3 and 6): the streams hand over what they bring, and every <see cref="Tick" /> one batch goes to the store
///     in one transaction. A closed candle covers its minute; a minute of trades goes out <see cref="TradeMinutesService.CloseMargin" />
///     after its end without coverage; liquidations go every <see cref="LiquidationsEvery" />, covered by the time their stream was
///     connected; a point of open interest covers its step. A stream that is back gives its break. A batch that failed goes again with the next one. The handlers of the streams are
///     thread-safe.
/// </summary>
public sealed class LiveWriterService(IMarketStore store, TradeMinutesService trades, FormingBoardService board, ICollectorJournal journal, TimeProvider time)
{
    public static readonly TimeSpan Tick = TimeSpan.FromSeconds(1);

    public static readonly TimeSpan LiquidationsEvery = TimeSpan.FromSeconds(5);

    private readonly ConcurrentQueue<(Instrument, Candle)> _closed = new ConcurrentQueue<(Instrument, Candle)>();
    private static readonly TimeSpan Step = TimeSpan.FromMinutes(5);

    private readonly ConcurrentQueue<(Instrument, OpenInterestPoint)> _openInterest = new ConcurrentQueue<(Instrument, OpenInterestPoint)>();
    private readonly ConcurrentQueue<(Instrument, LiquidationEvent)> _liquidations = new ConcurrentQueue<(Instrument, LiquidationEvent)>();
    private readonly ConcurrentQueue<StreamBreak> _breaks = new ConcurrentQueue<StreamBreak>();

    private readonly Lock _lock = new Lock();
    private readonly Dictionary<LiveStream, DateTimeOffset> _lostAt = [];
    private readonly List<(TimeRange Period, IReadOnlyList<Instrument> Pairs)> _liquidationsCovered = [];
    private IReadOnlyList<Instrument> _liquidationPairs = [];
    private DateTimeOffset? _liquidationsConnectedFrom;

    private LiveBatch _failed = LiveBatch.Empty;

    /// <summary>
    ///     The streams were subscribed at <paramref name="subscribedAt" />. Scenario «Перезапуск сервиса»: every stream was broken from the end of the
    ///     last written minute until then.
    /// </summary>
    public async Task StartAsync(IReadOnlyList<Instrument> liquidationPairs, DateTimeOffset subscribedAt, CancellationToken cancellationToken)
    {
        if (await store.GetLastMinuteEndAsync(cancellationToken) is { } written)
        {
            foreach (LiveStream stream in Enum.GetValues<LiveStream>())
            {
                _breaks.Enqueue(new StreamBreak(stream, written, subscribedAt));
            }
        }

        lock (_lock)
        {
            _liquidationPairs = liquidationPairs;
            _liquidationsConnectedFrom = subscribedAt;
        }
    }

    /// <summary>The streams were subscribed again for a new list of pairs; the liquidations of <paramref name="liquidationPairs" /> are covered from now on.</summary>
    public void Subscribed(IReadOnlyList<Instrument> liquidationPairs)
    {
        lock (_lock)
        {
            _liquidationPairs = liquidationPairs;
        }
    }

    /// <summary>A candle still forming goes to the board only.</summary>
    public void Forming(Instrument instrument, Candle candle)
    {
        board.Update(instrument, candle);
    }

    public void Closed(Instrument instrument, Candle candle)
    {
        _closed.Enqueue((instrument, candle));
    }

    public void Traded(Instrument instrument, TradeEvent trade)
    {
        if (trades.Add(instrument, trade) is { } current)
        {
            board.Update(instrument, current);
        }
    }

    /// <summary>A point of open interest covers its 5 minute step.</summary>
    public void OpenInterest(Instrument instrument, OpenInterestPoint point)
    {
        _openInterest.Enqueue((instrument, point));
    }

    public void Liquidated(Instrument instrument, LiquidationEvent liquidation)
    {
        _liquidations.Enqueue((instrument, liquidation));
    }

    /// <summary>A break starts at the first loss of the stream; the liquidations are covered until then.</summary>
    public void Lost(LiveStream stream, DateTimeOffset at)
    {
        lock (_lock)
        {
            _lostAt.TryAdd(stream, at);

            if (stream == LiveStream.Liquidations)
            {
                CoverLiquidationsUntil(at);
                _liquidationsConnectedFrom = null;
            }
        }
    }

    /// <summary>The stream is back: the break since its loss, if any, goes with the next batch.</summary>
    public void Restored(LiveStream stream, DateTimeOffset at)
    {
        lock (_lock)
        {
            if (_lostAt.Remove(stream, out DateTimeOffset lostAt))
            {
                _breaks.Enqueue(new StreamBreak(stream, lostAt, at));
            }

            if (stream == LiveStream.Liquidations)
            {
                _liquidationsConnectedFrom = at;
            }
        }
    }

    /// <summary>Flushes every <see cref="Tick" />, with the liquidations every <see cref="LiquidationsEvery" />, until cancelled.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(Tick, time);
        DateTimeOffset liquidationsDue = time.GetUtcNow() + LiquidationsEvery;

        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            bool withLiquidations = time.GetUtcNow() >= liquidationsDue;

            if (withLiquidations)
            {
                liquidationsDue = time.GetUtcNow() + LiquidationsEvery;
            }

            await FlushAsync(withLiquidations, cancellationToken);
        }
    }

    internal async Task FlushAsync(bool withLiquidations, CancellationToken cancellationToken)
    {
        (Instrument Instrument, Candle Candle)[] candles = Drain(_closed);
        (Instrument Instrument, OpenInterestPoint Point)[] openInterest = Drain(_openInterest);
        (Instrument Instrument, LiquidationEvent Liquidation)[] liquidations = withLiquidations ? Drain(_liquidations) : [];
        List<(SeriesKey, TimeRange)> coverage =
        [
            .. candles.Select(item => (SeriesKey.Candles(item.Instrument, Interval.OneMinute), new TimeRange(item.Candle.OpenTime, item.Candle.OpenTime.AddMinutes(1)))),
            .. openInterest.Select(item => (SeriesKey.Of(DataKind.OpenInterest, item.Instrument), new TimeRange(item.Point.Time, item.Point.Time + Step)))
        ];

        if (withLiquidations)
        {
            coverage.AddRange(TakeLiquidationCoverage());
        }

        LiveBatch batch = Join(_failed, new LiveBatch(candles, trades.TakeClosed(), liquidations, openInterest, coverage, Drain(_breaks)));

        if (batch.Candles.Count + batch.TradeMinutes.Count + batch.Liquidations.Count + batch.Coverage.Count + batch.Breaks.Count == 0)
        {
            return;
        }

        try
        {
            await store.WriteLiveAsync(batch, cancellationToken);
            _failed = LiveBatch.Empty;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            journal.Failed("live", exception);
            _failed = batch;
        }
    }

    private static T[] Drain<T>(ConcurrentQueue<T> queue)
    {
        var items = new List<T>();

        while (queue.TryDequeue(out T? item))
        {
            items.Add(item);
        }

        return [.. items];
    }

    private static LiveBatch Join(LiveBatch first, LiveBatch then)
    {
        return new LiveBatch(
            [.. first.Candles, .. then.Candles],
            [.. first.TradeMinutes, .. then.TradeMinutes],
            [.. first.Liquidations, .. then.Liquidations],
            [.. first.OpenInterest, .. then.OpenInterest],
            [.. first.Coverage, .. then.Coverage],
            [.. first.Breaks, .. then.Breaks]);
    }

    private IReadOnlyList<(SeriesKey, TimeRange)> TakeLiquidationCoverage()
    {
        lock (_lock)
        {
            DateTimeOffset now = time.GetUtcNow();
            CoverLiquidationsUntil(now);
            _liquidationsConnectedFrom = _liquidationsConnectedFrom is null ? null : now;
            (SeriesKey, TimeRange)[] coverage =
                [.. _liquidationsCovered.SelectMany(covered => covered.Pairs.Select(pair => (SeriesKey.Of(DataKind.Liquidations, pair), covered.Period)))];
            _liquidationsCovered.Clear();

            return coverage;
        }
    }

    private void CoverLiquidationsUntil(DateTimeOffset until)
    {
        if (_liquidationsConnectedFrom is { } from && until > from)
        {
            _liquidationsCovered.Add((new TimeRange(from, until), _liquidationPairs));
        }
    }
}
