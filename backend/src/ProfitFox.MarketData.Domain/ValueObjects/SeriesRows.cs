using ProfitFox.MarketData.Domain.Entities;

namespace ProfitFox.MarketData.Domain.ValueObjects;

/// <summary>The data of a series over a period, oldest first; only the list of the kind of the series is filled.</summary>
public sealed record SeriesRows(
    IReadOnlyList<Candle> Candles,
    IReadOnlyList<OpenInterestPoint> OpenInterest,
    IReadOnlyList<FundingRatePoint> Funding,
    IReadOnlyList<LiquidationEvent> Liquidations,
    IReadOnlyList<TradeFlowMinute> TradeFlow)
{
    public static readonly SeriesRows Empty = new SeriesRows([], [], [], [], []);

    public static SeriesRows OfCandles(IReadOnlyList<Candle> candles)
    {
        return Empty with { Candles = candles };
    }

    public static SeriesRows OfOpenInterest(IReadOnlyList<OpenInterestPoint> points)
    {
        return Empty with { OpenInterest = points };
    }

    public static SeriesRows OfFunding(IReadOnlyList<FundingRatePoint> rates)
    {
        return Empty with { Funding = rates };
    }
}