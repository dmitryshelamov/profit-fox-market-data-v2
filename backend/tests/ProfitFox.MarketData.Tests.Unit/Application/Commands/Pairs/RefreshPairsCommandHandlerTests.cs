using System.Globalization;

using NSubstitute;

using ProfitFox.MarketData.Application.Commands.Pairs.RefreshPairs;
using ProfitFox.MarketData.Application.Ports;
using ProfitFox.MarketData.Domain.Entities;
using ProfitFox.MarketData.Domain.Entities.Enums;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects;

using Shouldly;

namespace ProfitFox.MarketData.Tests.Unit.Application.Commands.Pairs;

/// <summary>Requirement «Пары сервиса»: the list of the exchange is read again, the pairs that changed are saved, the listed ones are collected.</summary>
public class RefreshPairsCommandHandlerTests
{
    private static readonly DateTimeOffset Launch = DateTimeOffset.Parse("2026-11-01T10:00:00Z", CultureInfo.InvariantCulture);
    private static readonly Instrument Btc = new Instrument(MarketCategory.Linear, "BTCUSDT");
    private static readonly Instrument Sol = new Instrument(MarketCategory.Linear, "SOLUSDT");
    private static readonly Instrument Eth = new Instrument(MarketCategory.Spot, "ETHUSDT");

    private IExchangeMarketData _exchange = null!;
    private IMarketPairRepository _pairs = null!;
    private RefreshPairsCommandHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _exchange = Substitute.For<IExchangeMarketData>();
        _pairs = Substitute.For<IMarketPairRepository>();
        _handler = new RefreshPairsCommandHandler(_exchange, _pairs);
    }

    // Scenario «Новая пара».
    [Test]
    public void New_pair_of_the_list_is_saved_and_collected()
    {
        _pairs.GetAllAsync(Arg.Any<CancellationToken>()).Returns([new MarketPair(Eth, true, null)]);
        List(MarketCategory.Spot, Listed(Eth));
        List(MarketCategory.Linear, Listed(Btc) with { ListedAt = Launch });

        IReadOnlyList<MarketPair> listed = Refresh();

        listed.ShouldBe([new MarketPair(Eth, true, null), new MarketPair(Btc, true, Launch)], ignoreOrder: true);
        _pairs.Received(1).SaveAsync(
            Arg.Is<IReadOnlyCollection<MarketPair>>(changed => changed!.SequenceEqual(new[] { new MarketPair(Btc, true, Launch) })),
            Arg.Any<CancellationToken>());
    }

    // Scenario «Пара снята с торгов».
    [Test]
    public void Vanished_pair_is_saved_as_not_listed_and_no_longer_collected()
    {
        _pairs.GetAllAsync(Arg.Any<CancellationToken>()).Returns([new MarketPair(Btc, true, Launch), new MarketPair(Sol, true, null)]);
        List(MarketCategory.Spot);
        List(MarketCategory.Linear, Listed(Btc) with { ListedAt = Launch });

        IReadOnlyList<MarketPair> listed = Refresh();

        listed.ShouldBe([new MarketPair(Btc, true, Launch)]);
        _pairs.Received(1).SaveAsync(
            Arg.Is<IReadOnlyCollection<MarketPair>>(changed => changed!.SequenceEqual(new[] { new MarketPair(Sol, false, null) })),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public void Symbols_of_the_command_narrow_the_list()
    {
        _pairs.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);
        List(MarketCategory.Spot, Listed(Eth));
        List(MarketCategory.Linear, Listed(Btc), Listed(Sol));

        IReadOnlyList<MarketPair> listed = Refresh(["BTCUSDT"]);

        listed.ShouldBe([new MarketPair(Btc, true, null)]);
    }

    private IReadOnlyList<MarketPair> Refresh(IReadOnlyCollection<string>? symbols = null)
    {
        return _handler.Handle(new RefreshPairsCommand(symbols ?? []), CancellationToken.None).GetAwaiter().GetResult();
    }

    private void List(MarketCategory category, params ListedInstrument[] instruments)
    {
        _exchange.ListInstrumentsAsync(category, Arg.Any<CancellationToken>()).Returns(instruments);
    }

    private static ListedInstrument Listed(Instrument instrument)
    {
        return new ListedInstrument(instrument.Category, instrument.Symbol, "USDT", Trading: true, Perpetual: instrument.Category == MarketCategory.Linear);
    }
}
