using ProfitFox.MarketData.Domain.Entities.Enums;

namespace ProfitFox.MarketData.Domain.ValueObjects;

/// <summary>A traded contract: the same symbol in different market categories is a different instrument.</summary>
public sealed record Instrument
{
    public MarketCategory Category { get; }

    public string Symbol { get; }

    public Instrument(MarketCategory category, string symbol)
    {
        Category = category;
        Symbol = symbol.ToUpperInvariant();
    }
}