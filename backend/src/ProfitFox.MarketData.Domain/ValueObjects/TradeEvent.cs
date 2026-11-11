using ProfitFox.MarketData.Domain.Entities.Enums;

namespace ProfitFox.MarketData.Domain.ValueObjects;

/// <summary>A public trade as the stream or the daily archive of the exchange gives it: its time, the side of its taker, the volume in coins and the price.</summary>
public sealed record TradeEvent(DateTimeOffset Time, TradeSide Side, decimal Quantity, decimal Price);