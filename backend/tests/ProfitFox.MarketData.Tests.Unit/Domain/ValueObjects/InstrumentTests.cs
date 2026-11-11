using ProfitFox.MarketData.Domain.Entities.Enums;
using ProfitFox.MarketData.Domain.ValueObjects;

using Shouldly;

namespace ProfitFox.MarketData.Tests.Unit.Domain.ValueObjects;

public class InstrumentTests
{
    [Test]
    public void Symbol_is_kept_in_upper_case()
    {
        new Instrument(MarketCategory.Linear, "btcUsdt").Symbol.ShouldBe("BTCUSDT");
    }

    [Test]
    public void Same_symbol_in_another_category_is_another_instrument()
    {
        new Instrument(MarketCategory.Spot, "BTCUSDT").ShouldNotBe(new Instrument(MarketCategory.Linear, "BTCUSDT"));
    }
}