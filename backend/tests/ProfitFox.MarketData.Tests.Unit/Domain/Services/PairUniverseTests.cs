using System.Globalization;

using ProfitFox.MarketData.Domain.Entities;
using ProfitFox.MarketData.Domain.Entities.Enums;
using ProfitFox.MarketData.Domain.Services;
using ProfitFox.MarketData.Domain.ValueObjects;

using Shouldly;

namespace ProfitFox.MarketData.Tests.Unit.Domain.Services;

/// <summary>Requirement «Пары сервиса»: every trading USDT pair of spot and every trading USDT perpetual of linear.</summary>
public class PairUniverseTests
{
    private static readonly DateTimeOffset Launch = DateTimeOffset.Parse("2026-11-01T10:00:00Z", CultureInfo.InvariantCulture);
    private static readonly Instrument Btc = new Instrument(MarketCategory.Linear, "BTCUSDT");
    private static readonly Instrument Eth = new Instrument(MarketCategory.Spot, "ETHUSDT");

    [Test]
    public void Trading_usdt_spot_pair_and_usdt_perpetual_are_kept()
    {
        ListedInstrument spot = Listed(MarketCategory.Spot, "ETHUSDT");
        ListedInstrument perpetual = Listed(MarketCategory.Linear, "BTCUSDT");

        PairUniverse.Select([spot, perpetual], []).ShouldBe([spot, perpetual]);
    }

    [Test]
    public void Pair_out_of_trading_is_left_out()
    {
        PairUniverse.Select([Listed(MarketCategory.Spot, "ETHUSDT") with { Trading = false }], []).ShouldBeEmpty();
    }

    [Test]
    public void Pair_quoted_in_another_coin_is_left_out()
    {
        PairUniverse.Select([Listed(MarketCategory.Spot, "ETHBTC") with { QuoteCoin = "BTC" }], []).ShouldBeEmpty();
    }

    [Test]
    public void Linear_contract_with_an_expiry_is_left_out()
    {
        PairUniverse.Select([Listed(MarketCategory.Linear, "BTCUSDT-26DEC26") with { Perpetual = false }], []).ShouldBeEmpty();
    }

    [Test]
    public void Symbols_of_the_settings_narrow_the_pairs_in_any_case()
    {
        ListedInstrument btc = Listed(MarketCategory.Linear, "BTCUSDT");

        PairUniverse.Select([btc, Listed(MarketCategory.Linear, "SOLUSDT")], ["btcusdt"]).ShouldBe([btc]);
    }

    // Scenario «Новая пара».
    [Test]
    public void New_pair_is_added_with_its_launch()
    {
        PairUniverse.Merge([], [Listed(MarketCategory.Linear, "BTCUSDT") with { ListedAt = Launch }])
            .ShouldBe([new MarketPair(Btc, Listed: true, Launch)]);
    }

    // Scenario «Пара снята с торгов».
    [Test]
    public void Vanished_pair_is_no_longer_listed()
    {
        PairUniverse.Merge([new MarketPair(Btc, Listed: true, Launch)], []).ShouldBe([new MarketPair(Btc, Listed: false, Launch)]);
    }

    [Test]
    public void Pair_back_in_trading_is_listed_again()
    {
        PairUniverse.Merge([new MarketPair(Eth, Listed: false, ListedAt: null)], [Listed(MarketCategory.Spot, "ETHUSDT")])
            .ShouldBe([new MarketPair(Eth, Listed: true, ListedAt: null)]);
    }

    [Test]
    public void Pair_that_did_not_change_is_not_returned()
    {
        PairUniverse.Merge([new MarketPair(Btc, Listed: true, Launch)], [Listed(MarketCategory.Linear, "BTCUSDT") with { ListedAt = Launch }])
            .ShouldBeEmpty();
    }

    private static ListedInstrument Listed(MarketCategory category, string symbol)
    {
        return new ListedInstrument(category, symbol, "USDT", Trading: true, category == MarketCategory.Linear);
    }
}