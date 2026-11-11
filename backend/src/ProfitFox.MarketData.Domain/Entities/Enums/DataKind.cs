namespace ProfitFox.MarketData.Domain.Entities.Enums;

/// <summary>A kind of data the service collects: candles of an interval, open interest, funding, liquidations and minutes of trades.</summary>
public enum DataKind
{
    Candles,
    OpenInterest,
    Funding,
    Liquidations,
    TradeFlow
}