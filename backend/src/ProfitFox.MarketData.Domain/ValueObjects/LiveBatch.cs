using ProfitFox.MarketData.Domain.Entities;

namespace ProfitFox.MarketData.Domain.ValueObjects;

/// <summary>
///     What the live lane writes in one transaction: closed minute candles, live minutes of trades, liquidations, points of
///     open interest, the coverage they give and
///     the breaks of the streams.
/// </summary>
public sealed record LiveBatch(
    IReadOnlyList<(Instrument Instrument, Candle Candle)> Candles,
    IReadOnlyList<(Instrument Instrument, TradeFlowMinute Minute)> TradeMinutes,
    IReadOnlyList<(Instrument Instrument, LiquidationEvent Liquidation)> Liquidations,
    IReadOnlyList<(Instrument Instrument, OpenInterestPoint Point)> OpenInterest,
    IReadOnlyList<(SeriesKey Series, TimeRange Period)> Coverage,
    IReadOnlyList<StreamBreak> Breaks)
{
    public static readonly LiveBatch Empty = new LiveBatch([], [], [], [], [], []);
}
