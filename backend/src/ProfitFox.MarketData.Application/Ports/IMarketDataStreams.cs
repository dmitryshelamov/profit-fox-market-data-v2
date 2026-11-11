using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Domain.Entities;
using ProfitFox.MarketData.Domain.ValueObjects;

namespace ProfitFox.MarketData.Application.Ports;

/// <summary>
///     The live public streams of the exchange, each on connections of its own, so that the heavy stream of trades does not hold back the
///     closed candles and a break of one stream does not touch the others.
/// </summary>
/// <remarks>
///     A subscription throws <see cref="ExchangeUnavailableException" /> when the exchange refuses it. After that it lives until disposed:
///     the adapter reconnects and subscribes again by itself, and tells the break with <c>lost</c> when the first connection of the stream
///     is lost and <c>restored</c> when the last one is back, both at the time of the service.
/// </remarks>
public interface IMarketDataStreams
{
    /// <summary>
    ///     The minute candles of the instruments: an update of a candle still forming goes to <paramref name="forming" />, the closed candle
    ///     of the minute to <paramref name="closed" />.
    /// </summary>
    Task<IAsyncDisposable> SubscribeMinuteCandlesAsync(
        IReadOnlyList<Instrument> instruments,
        Action<Instrument, Candle> forming,
        Action<Instrument, Candle> closed,
        Action<DateTimeOffset> lost,
        Action<DateTimeOffset> restored);

    /// <summary>The liquidations of the instruments, which are linear pairs: the exchange streams none of spot.</summary>
    Task<IAsyncDisposable> SubscribeLiquidationsAsync(
        IReadOnlyList<Instrument> instruments,
        Action<Instrument, LiquidationEvent> liquidated,
        Action<DateTimeOffset> lost,
        Action<DateTimeOffset> restored);

    /// <summary>Every public trade of the instruments, with the side of its taker.</summary>
    Task<IAsyncDisposable> SubscribeTradesAsync(
        IReadOnlyList<Instrument> instruments,
        Action<Instrument, TradeEvent> traded,
        Action<DateTimeOffset> lost,
        Action<DateTimeOffset> restored);
}
