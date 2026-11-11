using System.Globalization;

using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitFox.MarketData.Application.Commands.History.TopUpFunding;
using ProfitFox.MarketData.Application.Ports;
using ProfitFox.MarketData.Domain.Entities;
using ProfitFox.MarketData.Domain.Entities.Enums;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects;

using Shouldly;

namespace ProfitFox.MarketData.Tests.Unit.Application.Commands.History;

/// <summary>Decision 5, task 5.4: the funding of the linear pairs is topped up from the end of its coverage, as the scanner does.</summary>
public class TopUpFundingCommandHandlerTests
{
    private static readonly DateTimeOffset Now = Time("2026-11-10T16:10:00Z");
    private static readonly Instrument Btc = new Instrument(MarketCategory.Linear, "BTCUSDT");
    private static readonly Instrument Eth = new Instrument(MarketCategory.Spot, "ETHUSDT");

    private IExchangeMarketData _exchange = null!;
    private IMarketStore _store = null!;
    private TopUpFundingCommandHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _exchange = Substitute.For<IExchangeMarketData>();
        _store = Substitute.For<IMarketStore>();
        _store.GetCoverageAsync(Arg.Any<SeriesKey>(), Arg.Any<CancellationToken>()).Returns(Coverage.Empty);
        _exchange.GetFundingAsync(default!, from: default, to: default, cancellationToken: default).ReturnsForAnyArgs([]);
        _handler = new TopUpFundingCommandHandler(_exchange, _store, new FakeTimeProvider(Now));
    }

    // Scenario «Списание фандинга»: the settlement of 16:00 is stored at the next top-up after it.
    [Test]
    public async Task Funding_is_loaded_from_the_end_of_its_coverage_until_now()
    {
        SeriesKey funding = SeriesKey.Of(DataKind.Funding, Btc);
        var settlement = new FundingRatePoint(Time("2026-11-10T16:00:00Z"), Rate: 0.0001m);
        _store.GetCoverageAsync(funding, Arg.Any<CancellationToken>())
            .Returns(Coverage.Empty.Add(new TimeRange(Time("2026-11-01T00:00:00Z"), Time("2026-11-02T00:00:00Z"))).Add(new TimeRange(Time("2026-11-09T00:00:00Z"), Time("2026-11-10T15:00:00Z"))));
        _exchange.GetFundingAsync(Btc, Time("2026-11-10T15:00:00Z"), Now, Arg.Any<CancellationToken>()).Returns([settlement]);

        await _handler.Handle(new TopUpFundingCommand([new MarketPair(Btc, Listed: true, ListedAt: null)]), CancellationToken.None);

        await _store.Received(1).ReplaceAsync(
            funding,
            new TimeRange(Time("2026-11-10T15:00:00Z"), Now),
            Arg.Is<SeriesRows>(rows => rows!.Funding.SequenceEqual(new[] { settlement })),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Pair_without_funding_coverage_is_left_to_its_history()
    {
        await _handler.Handle(new TopUpFundingCommand([new MarketPair(Btc, Listed: true, ListedAt: null)]), CancellationToken.None);

        await _exchange.DidNotReceiveWithAnyArgs().GetFundingAsync(default!, from: default, to: default, cancellationToken: default);
    }

    // Spot has no funding.
    [Test]
    public async Task Spot_pair_has_no_funding()
    {
        _store.GetCoverageAsync(Arg.Any<SeriesKey>(), Arg.Any<CancellationToken>()).Returns(Coverage.Empty.Add(new TimeRange(Time("2026-11-10T15:00:00Z"), Time("2026-11-10T16:00:00Z"))));

        await _handler.Handle(new TopUpFundingCommand([new MarketPair(Eth, Listed: true, ListedAt: null)]), CancellationToken.None);

        await _exchange.DidNotReceiveWithAnyArgs().GetFundingAsync(default!, from: default, to: default, cancellationToken: default);
    }

    private static DateTimeOffset Time(string text)
    {
        return DateTimeOffset.Parse(text, CultureInfo.InvariantCulture);
    }
}
