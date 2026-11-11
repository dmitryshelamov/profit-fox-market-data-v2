using AutoFixture;

using ProfitFox.MarketData.Domain.Entities.Enums;

namespace ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entity.Enums;

public static class MarketCategoryGenerator
{
    private static readonly Fixture Fixture = new Fixture();

    public static MarketCategory Generate()
    {
        return Fixture.Create<MarketCategory>();
    }
}