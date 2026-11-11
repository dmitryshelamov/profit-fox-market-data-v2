using MediatR;

using ProfitFox.MarketData.Application.Commands.History.TopUpFunding;
using ProfitFox.MarketData.Application.Commands.Pairs.RefreshPairs;
using ProfitFox.MarketData.Application.Ports;
using ProfitFox.MarketData.Domain.Entities;
using ProfitFox.MarketData.Domain.Entities.Enums;
using ProfitFox.MarketData.Domain.ValueObjects;

namespace ProfitFox.MarketData.Application.Services.Collection;

/// <summary>
///     The collector (decisions 4 and 5): the streams follow the list of pairs and feed the live lane. A new list subscribes every stream
///     again, as the scanner does; the time in between is a break of every stream.
/// </summary>
public sealed class CollectorSessionService(
    IMediator mediator,
    IMarketDataStreams streams,
    LiveWriterService writer,
    IExchangeMarketData exchange,
    ICollectorJournal journal,
    CollectorSettings settings,
    TimeProvider time)
{
    public static readonly TimeSpan PairsEvery = TimeSpan.FromMinutes(10);

    public static readonly TimeSpan FundingEvery = TimeSpan.FromMinutes(10);

    public static readonly TimeSpan Retry = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan OpenInterestRetry = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan OneMinute = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Step = TimeSpan.FromMinutes(5);

    private IReadOnlyList<MarketPair> _pairs = [];
    private IAsyncDisposable[] _subscriptions = [];
    private bool _started;

    /// <summary>
    ///     Collects until cancelled: once the pairs are subscribed, the live lane runs, the list is read again every <see cref="PairsEvery" />,
    ///     the tickers at the start of every step and the funding every <see cref="FundingEvery" />. A failed step goes to the journal and is
    ///     tried again.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RefreshUntilDoneAsync(cancellationToken);
            await Task.WhenAll(writer.RunAsync(cancellationToken), PairsAsync(cancellationToken), OpenInterestAsync(cancellationToken), FundingAsync(cancellationToken));
        }
        finally
        {
            await DisposeSubscriptionsAsync();
        }
    }

    /// <summary>Reads the list of pairs again and subscribes the streams to it when it changed or nothing is subscribed.</summary>
    /// <remarks>A refused subscription or a live lane that did not start leaves no subscription, so the next refresh subscribes again.</remarks>
    internal async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<MarketPair> listed = await mediator.Send(new RefreshPairsCommand(settings.Symbols), cancellationToken);
        Instrument[] instruments = [.. listed.Select(pair => pair.Instrument)];

        if (_subscriptions.Length > 0 && instruments.ToHashSet().SetEquals(_pairs.Select(pair => pair.Instrument)))
        {
            return;
        }

        if (_subscriptions.Length > 0)
        {
            DateTimeOffset lostAt = time.GetUtcNow();

            foreach (LiveStream stream in Enum.GetValues<LiveStream>())
            {
                writer.Lost(stream, lostAt);
            }

            await DisposeSubscriptionsAsync();
        }

        Instrument[] linear = [.. instruments.Where(instrument => instrument.Category == MarketCategory.Linear)];
        await SubscribeAsync(instruments, linear);
        Volatile.Write(ref _pairs, listed);
        DateTimeOffset subscribedAt = time.GetUtcNow();

        if (!_started)
        {
            try
            {
                await writer.StartAsync(linear, subscribedAt, cancellationToken);
            }
            catch
            {
                await DisposeSubscriptionsAsync();

                throw;
            }

            _started = true;

            return;
        }

        writer.Subscribed(linear);

        foreach (LiveStream stream in Enum.GetValues<LiveStream>())
        {
            writer.Restored(stream, subscribedAt);
        }
    }

    /// <summary>
    ///     The open interest of the start of the step from the tickers, as the scanner reads it: only a read in the first minute of the step
    ///     tells it, a later one leaves the step to the history.
    /// </summary>
    internal async Task ReadOpenInterestAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = time.GetUtcNow();
        DateTimeOffset step = StepOf(now);

        if (now - step >= OneMinute)
        {
            return;
        }

        HashSet<Instrument> listed = [.. Volatile.Read(ref _pairs).Select(pair => pair.Instrument)];

        foreach ((Instrument instrument, decimal openInterest) in await exchange.GetOpenInterestNowAsync(cancellationToken))
        {
            if (listed.Contains(instrument))
            {
                writer.OpenInterest(instrument, new OpenInterestPoint(step, openInterest));
            }
        }
    }

    private static DateTimeOffset StepOf(DateTimeOffset now)
    {
        return now.AddTicks(-(now.UtcTicks % Step.Ticks));
    }

    private async Task RefreshUntilDoneAsync(CancellationToken cancellationToken)
    {
        while (!await TryAsync("pairs", RefreshAsync, cancellationToken))
        {
            await Task.Delay(Retry, time, cancellationToken);
        }
    }

    private async Task PairsAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            await Task.Delay(PairsEvery, time, cancellationToken);
            await RefreshUntilDoneAsync(cancellationToken);
        }
    }

    private async Task OpenInterestAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            while (!await TryAsync("open-interest", ReadOpenInterestAsync, cancellationToken))
            {
                await Task.Delay(OpenInterestRetry, time, cancellationToken);
            }

            DateTimeOffset now = time.GetUtcNow();
            await Task.Delay(StepOf(now) + Step - now, time, cancellationToken);
        }
    }

    private async Task FundingAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            await TryAsync("funding", token => mediator.Send(new TopUpFundingCommand(Volatile.Read(ref _pairs)), token), cancellationToken);
            await Task.Delay(FundingEvery, time, cancellationToken);
        }
    }

    private async Task<bool> TryAsync(string step, Func<CancellationToken, Task> work, CancellationToken cancellationToken)
    {
        try
        {
            await work(cancellationToken);

            return true;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            journal.Failed(step, exception);

            return false;
        }
    }

    private async Task SubscribeAsync(Instrument[] instruments, Instrument[] linear)
    {
        var made = new List<IAsyncDisposable>();

        try
        {
            made.Add(
                await streams.SubscribeMinuteCandlesAsync(
                    instruments,
                    writer.Forming,
                    writer.Closed,
                    at => writer.Lost(LiveStream.Candles, at),
                    at => writer.Restored(LiveStream.Candles, at)));
            made.Add(
                await streams.SubscribeTradesAsync(
                    instruments,
                    writer.Traded,
                    at => writer.Lost(LiveStream.Trades, at),
                    at => writer.Restored(LiveStream.Trades, at)));
            made.Add(
                await streams.SubscribeLiquidationsAsync(
                    linear,
                    writer.Liquidated,
                    at => writer.Lost(LiveStream.Liquidations, at),
                    at => writer.Restored(LiveStream.Liquidations, at)));
        }
        catch
        {
            foreach (IAsyncDisposable subscription in made)
            {
                await subscription.DisposeAsync();
            }

            throw;
        }

        _subscriptions = [.. made];
    }

    private async Task DisposeSubscriptionsAsync()
    {
        foreach (IAsyncDisposable subscription in _subscriptions)
        {
            await subscription.DisposeAsync();
        }

        _subscriptions = [];
    }
}
