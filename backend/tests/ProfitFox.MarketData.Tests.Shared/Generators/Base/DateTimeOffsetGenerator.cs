using AutoFixture;

namespace ProfitFox.MarketData.Tests.Shared.Generators.Base;

public static class DateTimeOffsetGenerator
{
    private static readonly Fixture Fixture = new Fixture();

    /// <summary>A UTC moment with millisecond precision: PostgreSQL keeps microseconds and Bybit reports milliseconds.</summary>
    public static DateTimeOffset Generate()
    {
        DateTimeOffset value = Fixture.Create<DateTimeOffset>().ToUniversalTime();

        return value.AddTicks(-(value.Ticks % TimeSpan.TicksPerMillisecond));
    }
}