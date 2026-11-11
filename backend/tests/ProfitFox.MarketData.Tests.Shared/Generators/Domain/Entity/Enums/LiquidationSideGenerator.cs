using AutoFixture;

using ProfitFox.MarketData.Domain.Entities.Enums;

namespace ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entity.Enums;

public static class LiquidationSideGenerator
{
    private static readonly Fixture Fixture = new Fixture();

    public static LiquidationSide Generate()
    {
        return Fixture.Create<LiquidationSide>();
    }
}