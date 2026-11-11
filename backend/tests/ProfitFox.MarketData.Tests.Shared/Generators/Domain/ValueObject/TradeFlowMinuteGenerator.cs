using AutoFixture;

using ProfitFox.MarketData.Domain.ValueObjects;
using ProfitFox.MarketData.Tests.Shared.Generators.Base;

namespace ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObject;

public static class TradeFlowMinuteGenerator
{
    private static readonly Fixture Fixture = new Fixture();

    /// <summary>A minute of trades that opens at <paramref name="openTime" />, a whole minute UTC by default.</summary>
    public static TradeFlowMinute Generate(DateTimeOffset? openTime = null)
    {
        DateTimeOffset moment = DateTimeOffsetGenerator.Generate();

        return new TradeFlowMinute(
            openTime ?? moment.AddTicks(-(moment.UtcTicks % TimeSpan.TicksPerMinute)),
            DecimalGenerator.Generate(),
            DecimalGenerator.Generate(),
            DecimalGenerator.Generate(),
            DecimalGenerator.Generate(),
            Fixture.Create<int>(),
            Fixture.Create<int>());
    }
}