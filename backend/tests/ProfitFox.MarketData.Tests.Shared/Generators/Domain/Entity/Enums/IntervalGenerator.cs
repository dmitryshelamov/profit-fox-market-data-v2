using AutoFixture;

using ProfitFox.MarketData.Domain.Entities.Enums;

namespace ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entity.Enums;

public static class IntervalGenerator
{
    private static readonly Fixture Fixture = new Fixture();

    public static Interval Generate()
    {
        return Fixture.Create<Interval>();
    }
}