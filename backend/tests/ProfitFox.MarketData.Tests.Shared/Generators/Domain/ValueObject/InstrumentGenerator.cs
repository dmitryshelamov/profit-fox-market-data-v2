using AutoFixture;

using ProfitFox.MarketData.Domain.Entities.Enums;
using ProfitFox.MarketData.Domain.ValueObjects;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entity.Enums;

namespace ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObject;

public static class InstrumentGenerator
{
    private static readonly Fixture Fixture = new Fixture();

    /// <summary>A USDT pair of its own letters, so two generated pairs never collide.</summary>
    public static Instrument Generate(MarketCategory? category = null)
    {
        var coin = new string([.. Fixture.Create<string>().Where(char.IsLetter).Take(8)]);

        return new Instrument(category ?? MarketCategoryGenerator.Generate(), coin + "USDT");
    }
}