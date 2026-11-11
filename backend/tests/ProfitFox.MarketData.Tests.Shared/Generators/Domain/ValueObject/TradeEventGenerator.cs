using ProfitFox.MarketData.Domain.ValueObjects;
using ProfitFox.MarketData.Tests.Shared.Generators.Base;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entity.Enums;

namespace ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObject;

public static class TradeEventGenerator
{
    public static TradeEvent Generate(DateTimeOffset? time = null)
    {
        return new TradeEvent(time ?? DateTimeOffsetGenerator.Generate(), TradeSideGenerator.Generate(), DecimalGenerator.Generate(), DecimalGenerator.Generate());
    }
}