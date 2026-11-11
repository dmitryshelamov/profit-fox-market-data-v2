using ProfitFox.MarketData.Domain.Entities.Enums;

namespace ProfitFox.MarketData.Domain.Entities;

/// <summary>
///     Trading aggregate of one instrument over one interval: prices, volume in coins and turnover in the quote coin, exactly as the
///     exchange reported them.
/// </summary>
public sealed record Candle(DateTimeOffset OpenTime, decimal Open, decimal High, decimal Low, decimal Close, decimal Volume, decimal Turnover)
{
    public bool IsClosedAt(Interval interval, DateTimeOffset now)
    {
        return interval.CandleEnd(OpenTime) <= now;
    }
}