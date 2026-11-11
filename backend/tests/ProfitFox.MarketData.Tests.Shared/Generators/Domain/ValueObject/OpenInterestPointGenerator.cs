using ProfitFox.MarketData.Domain.ValueObjects;
using ProfitFox.MarketData.Tests.Shared.Generators.Base;

namespace ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObject;

public static class OpenInterestPointGenerator
{
    public static OpenInterestPoint Generate(DateTimeOffset? time = null)
    {
        return new OpenInterestPoint(time ?? DateTimeOffsetGenerator.Generate(), DecimalGenerator.Generate());
    }
}