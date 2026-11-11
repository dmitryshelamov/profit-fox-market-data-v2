using Bybit.Net.Clients;
using Bybit.Net.Enums;
using Bybit.Net.Objects.Models.V5;
using Bybit.Net.Objects.Options;

using CryptoExchange.Net.Objects;
using CryptoExchange.Net.Objects.Sockets;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Application.Ports;
using ProfitFox.MarketData.Domain.Entities;
using ProfitFox.MarketData.Domain.Entities.Enums;
using ProfitFox.MarketData.Domain.ValueObjects;

using MarketCategory = ProfitFox.MarketData.Domain.Entities.Enums.MarketCategory;

namespace ProfitFox.MarketData.Infrastructure.Exchanges.Bybit;

/// <summary>
///     The public streams of Bybit v5 for the whole market (decision 5): every stream on a socket client of its own, <see cref="BatchSize" />
///     topics in a subscribe request, at most <see cref="TopicsPerConnection" /> topics on a connection. CryptoExchange.Net reconnects and
///     subscribes again by itself.
/// </summary>
internal sealed class BybitMarketDataStreams(ILoggerFactory loggers, BybitSettings settings, TimeProvider time) : IMarketDataStreams
{
    // The most topics Bybit takes in one subscribe request of spot; linear uses the same so that both lay out alike.
    private const int BatchSize = 10;

    private const int TopicsPerConnection = 100;

    public Task<IAsyncDisposable> SubscribeMinuteCandlesAsync(
        IReadOnlyList<Instrument> instruments,
        Action<Instrument, Candle> forming,
        Action<Instrument, Candle> closed,
        Action<DateTimeOffset> lost,
        Action<DateTimeOffset> restored)
    {
        return SubscribeAsync(
            instruments,
            lost,
            restored,
            (client, category, symbols) => category == MarketCategory.Spot
                ? client.V5SpotApi.SubscribeToKlineUpdatesAsync(symbols, KlineInterval.OneMinute, update => Klines(category, update, forming, closed), CancellationToken.None)
                : client.V5LinearApi.SubscribeToKlineUpdatesAsync(symbols, KlineInterval.OneMinute, update => Klines(category, update, forming, closed), CancellationToken.None));
    }

    public Task<IAsyncDisposable> SubscribeLiquidationsAsync(
        IReadOnlyList<Instrument> instruments,
        Action<Instrument, LiquidationEvent> liquidated,
        Action<DateTimeOffset> lost,
        Action<DateTimeOffset> restored)
    {
        return SubscribeAsync(
            instruments,
            lost,
            restored,
            (client, category, symbols) => client.V5LinearApi.SubscribeToAllLiquidationUpdatesAsync(symbols, update => Liquidations(category, update.Data, liquidated), CancellationToken.None));
    }

    public Task<IAsyncDisposable> SubscribeTradesAsync(
        IReadOnlyList<Instrument> instruments,
        Action<Instrument, TradeEvent> traded,
        Action<DateTimeOffset> lost,
        Action<DateTimeOffset> restored)
    {
        return SubscribeAsync(
            instruments,
            lost,
            restored,
            (client, category, symbols) => category == MarketCategory.Spot
                ? client.V5SpotApi.SubscribeToTradeUpdatesAsync(symbols, update => Trades(category, update.Data, traded), CancellationToken.None)
                : client.V5LinearApi.SubscribeToTradeUpdatesAsync(symbols, update => Trades(category, update.Data, traded), CancellationToken.None));
    }

    /// <summary>
    ///     One stream on a client of its own: the symbols of each category in batches, and one break for the breaks of all its subscriptions.
    ///     Each subscription is made with no token: CryptoExchange.Net closes a subscription when the token it was made with is cancelled.
    /// </summary>
    private async Task<IAsyncDisposable> SubscribeAsync(
        IReadOnlyList<Instrument> instruments,
        Action<DateTimeOffset> lost,
        Action<DateTimeOffset> restored,
        Func<BybitSocketClient, MarketCategory, string[], Task<WebSocketResult<UpdateSubscription>>> subscribe)
    {
        // The client puts this many topics on a connection before it opens another; it counts each topic of a batch.
        var client = new BybitSocketClient(
            Options.Create(new BybitSocketOptions { Environment = settings.Environment(), SocketIndividualSubscriptionCombineTarget = TopicsPerConnection }),
            loggers);
        var stream = new Stream(client);
        var breaks = new Breaks(lost, restored, time);

        try
        {
            foreach (IGrouping<MarketCategory, Instrument> category in instruments.GroupBy(instrument => instrument.Category))
            {
                foreach (Instrument[] batch in category.Chunk(BatchSize))
                {
                    WebSocketResult<UpdateSubscription> result = await subscribe(client, category.Key, [.. batch.Select(instrument => instrument.Symbol)]);

                    if (!result.Success)
                    {
                        throw new ExchangeUnavailableException($"Bybit refused the stream: {result.Error!.Message}");
                    }

                    result.Data.ConnectionLost += breaks.Lost;
                    result.Data.ConnectionRestored += _ => breaks.Restored();
                }
            }
        }
        catch
        {
            await stream.DisposeAsync();

            throw;
        }

        return stream;
    }

    private static void Klines(MarketCategory category, DataEvent<BybitKlineUpdate[]> update, Action<Instrument, Candle> forming, Action<Instrument, Candle> closed)
    {
        foreach (BybitKlineUpdate kline in update.Data)
        {
            (kline.Confirm ? closed : forming)(
                new Instrument(category, update.Symbol!),
                new Candle(Utc(kline.StartTime), kline.OpenPrice, kline.HighPrice, kline.LowPrice, kline.ClosePrice, kline.Volume, kline.Turnover));
        }
    }

    // Bybit gives the side of the liquidated position: "when you receive a Buy update, this means that a long position has been liquidated".
    private static void Liquidations(MarketCategory category, BybitLiquidationUpdate[] updates, Action<Instrument, LiquidationEvent> liquidated)
    {
        foreach (BybitLiquidationUpdate update in updates)
        {
            liquidated(
                new Instrument(category, update.Symbol),
                new LiquidationEvent(Utc(update.UpdateTime), update.Side == OrderSide.Buy ? LiquidationSide.Long : LiquidationSide.Short, update.Price, update.Quantity));
        }
    }

    // Every trade counts, a block trade and a trade of the RPI orders too, as in the daily archive.
    private static void Trades(MarketCategory category, BybitTrade[] trades, Action<Instrument, TradeEvent> traded)
    {
        foreach (BybitTrade trade in trades)
        {
            traded(
                new Instrument(category, trade.Symbol),
                new TradeEvent(Utc(trade.Timestamp), trade.Side == OrderSide.Buy ? TradeSide.Buy : TradeSide.Sell, trade.Quantity, trade.Price));
        }
    }

    private static DateTimeOffset Utc(DateTime time)
    {
        return new DateTimeOffset(DateTime.SpecifyKind(time, DateTimeKind.Utc));
    }

    /// <summary>One break of the stream for the breaks of its subscriptions: lost with the first lost, back when the last is back.</summary>
    private sealed class Breaks(Action<DateTimeOffset> lost, Action<DateTimeOffset> restored, TimeProvider time)
    {
        private int _down;

        public void Lost()
        {
            if (Interlocked.Increment(ref _down) == 1)
            {
                lost(time.GetUtcNow());
            }
        }

        public void Restored()
        {
            if (Interlocked.Decrement(ref _down) == 0)
            {
                restored(time.GetUtcNow());
            }
        }
    }

    private sealed class Stream(BybitSocketClient client) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await client.UnsubscribeAllAsync();
            client.Dispose();
        }
    }
}
