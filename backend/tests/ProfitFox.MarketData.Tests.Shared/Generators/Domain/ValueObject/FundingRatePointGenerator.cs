using ProfitFox.MarketData.Domain.ValueObjects;
using ProfitFox.MarketData.Tests.Shared.Generators.Base;

namespace ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObject;

public static class FundingRatePointGenerator
{
    /// <summary>A settlement with a small rate, as a fraction such as <c>0.000125</c>.</summary>
    public static FundingRatePoint Generate(DateTimeOffset? time = null)
    {
        return new FundingRatePoint(time ?? DateTimeOffsetGenerator.Generate(), DecimalGenerator.Generate() / 10000m);
    }
}