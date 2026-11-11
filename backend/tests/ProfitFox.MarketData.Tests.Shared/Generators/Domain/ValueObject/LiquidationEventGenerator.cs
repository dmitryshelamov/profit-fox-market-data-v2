using ProfitFox.MarketData.Domain.ValueObjects;
using ProfitFox.MarketData.Tests.Shared.Generators.Base;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entity.Enums;

namespace ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObject;

public static class LiquidationEventGenerator
{
    public static LiquidationEvent Generate(DateTimeOffset? time = null)
    {
        return new LiquidationEvent(
            time ?? DateTimeOffsetGenerator.Generate(),
            LiquidationSideGenerator.Generate(),
            DecimalGenerator.Generate(),
            DecimalGenerator.Generate());
    }
}