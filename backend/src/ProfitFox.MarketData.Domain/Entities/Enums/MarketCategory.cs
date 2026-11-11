namespace ProfitFox.MarketData.Domain.Entities.Enums;

/// <summary>A market of Bybit the service collects: <see cref="Spot" /> and <see cref="Linear" /> (USDT perpetuals).</summary>
public enum MarketCategory
{
    Spot,
    Linear
}