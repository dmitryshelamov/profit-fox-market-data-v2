using AutoFixture;

using ProfitFox.MarketData.Domain.Entities.Enums;

namespace ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entity.Enums;

public static class TradeSideGenerator
{
    private static readonly Fixture Fixture = new Fixture();

    public static TradeSide Generate()
    {
        return Fixture.Create<TradeSide>();
    }
}