namespace ProfitFox.MarketData.Domain.Entities.Enums;

/// <summary>A live stream of the exchange the service collects, each on connections of its own.</summary>
public enum LiveStream
{
    Candles,
    Trades,
    Liquidations
}
