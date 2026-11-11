using ProfitFox.MarketData.Domain.Entities.Enums;

namespace ProfitFox.MarketData.Domain.ValueObjects;

/// <summary>A liquidation as the stream of the exchange gives it: its time in milliseconds, the side of the liquidated position, the price and the volume in coins.</summary>
public sealed record LiquidationEvent(DateTimeOffset Time, LiquidationSide Side, decimal Price, decimal Quantity);