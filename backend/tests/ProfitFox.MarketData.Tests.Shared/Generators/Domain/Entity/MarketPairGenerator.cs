using ProfitFox.MarketData.Domain.Entities;
using ProfitFox.MarketData.Domain.ValueObjects;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObject;

namespace ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entity;

public static class MarketPairGenerator
{
    /// <summary>A listed pair launched at <paramref name="listedAt" />, none by default.</summary>
    public static MarketPair Generate(Instrument? instrument = null, bool listed = true, DateTimeOffset? listedAt = null)
    {
        return new MarketPair(instrument ?? InstrumentGenerator.Generate(), listed, listedAt);
    }
}