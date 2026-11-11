using AutoFixture;

namespace ProfitFox.MarketData.Tests.Shared.Generators.Base;

public static class DecimalGenerator
{
    private static readonly Fixture Fixture = new Fixture();

    /// <summary>A positive amount, such as a price or a volume, with a fractional part.</summary>
    public static decimal Generate()
    {
        return Fixture.Create<decimal>() + 0.125m;
    }
}