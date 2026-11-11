using ProfitFox.MarketData.Domain.Entities;
using ProfitFox.MarketData.Tests.Shared.Generators.Base;

namespace ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entity;

public static class CandleGenerator
{
    public static Candle Generate(DateTimeOffset? openTime = null, decimal? close = null)
    {
        return new Candle(
            openTime ?? DateTimeOffsetGenerator.Generate(),
            DecimalGenerator.Generate(),
            DecimalGenerator.Generate(),
            DecimalGenerator.Generate(),
            close ?? DecimalGenerator.Generate(),
            DecimalGenerator.Generate(),
            DecimalGenerator.Generate());
    }
}