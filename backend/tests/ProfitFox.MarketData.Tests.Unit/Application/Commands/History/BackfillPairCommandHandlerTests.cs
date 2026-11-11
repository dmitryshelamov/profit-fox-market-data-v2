using System.Globalization;

using Microsoft.Extensions.Time.Testing;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

using ProfitFox.MarketData.Application.Commands.History.BackfillPair;
using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Application.Ports;
using ProfitFox.MarketData.Domain.Entities;
using ProfitFox.MarketData.Domain.Entities.Enums;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects;

using Shouldly;

namespace ProfitFox.MarketData.Tests.Unit.Application.Commands.History;

/// <summary>Requirement «История при запуске»: the window of a pair is loaded series by series, from the freshest day back.</summary>
public class BackfillPairCommandHandlerTests
{
    private static readonly DateTimeOffset Now = Time("2026-11-10T12:30:20Z");
    private static readonly Instrument Btc = new Instrument(MarketCategory.Linear, "BTCUSDT");
    private static readonly Instrument Eth = new Instrument(MarketCategory.Spot, "ETHUSDT");

    private IExchangeMarketData _exchange = null!;
    private IMarketStore _store = null!;
    private BackfillPairCommandHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _exchange = Substitute.For<IExchangeMarketData>();
        _store = Substitute.For<IMarketStore>();
        _store.GetCoverageAsync(Arg.Any<SeriesKey>(), Arg.Any<CancellationToken>()).Returns(Coverage.Empty);
        _exchange.GetCandlesAsync(default!, interval: default, from: default, to: default, cancellationToken: default).ReturnsForAnyArgs([]);
        _exchange.GetOpenInterestAsync(default!, from: default, to: default, cancellationToken: default).ReturnsForAnyArgs([]);
        _exchange.GetFundingAsync(default!, from: default, to: default, cancellationToken: default).ReturnsForAnyArgs([]);
        _handler = new BackfillPairCommandHandler(_exchange, _store, new FakeTimeProvider(Now));
    }

    // Scenario «Первый запуск».
    [Test]
    public void Linear_pair_loads_the_window_of_its_minutes_open_interest_and_funding()
    {
        Backfill(new MarketPair(Btc, Listed: true, ListedAt: null), days: 1).ShouldBeTrue();

        _exchange.Received(1).GetCandlesAsync(Btc, Interval.OneMinute, Time("2026-11-09T12:30:00Z"), Time("2026-11-10T12:30:00Z"), Arg.Any<CancellationToken>());
        _exchange.Received(1).GetOpenInterestAsync(Btc, Time("2026-11-09T12:30:00Z"), Time("2026-11-10T12:30:00Z"), Arg.Any<CancellationToken>());
        _exchange.Received(1).GetFundingAsync(Btc, Time("2026-11-09T12:30:20Z"), Now, Arg.Any<CancellationToken>());
    }

    // Scenario «Число дней увеличили»: only what the coverage does not hold, the freshest first.
    [Test]
    public void Missing_parts_of_the_window_are_loaded_from_new_to_old()
    {
        var covered = new TimeRange(Time("2026-11-09T18:00:00Z"), Time("2026-11-10T06:00:00Z"));
        _store.GetCoverageAsync(SeriesKey.Candles(Eth, Interval.OneMinute), Arg.Any<CancellationToken>()).Returns(Coverage.Empty.Add(covered));

        Backfill(new MarketPair(Eth, Listed: true, ListedAt: null), days: 1);

        Received.InOrder(() =>
        {
            _exchange.GetCandlesAsync(Eth, Interval.OneMinute, covered.To, Time("2026-11-10T12:30:00Z"), Arg.Any<CancellationToken>());
            _exchange.GetCandlesAsync(Eth, Interval.OneMinute, Time("2026-11-09T12:30:00Z"), covered.From, Arg.Any<CancellationToken>());
        });
    }

    // Scenario «Пара моложе окна».
    [Test]
    public void Window_of_a_young_pair_starts_at_its_launch()
    {
        Backfill(new MarketPair(Eth, Listed: true, Time("2026-11-10T06:00:00Z")), days: 90);

        _exchange.Received(1).GetCandlesAsync(Eth, Interval.OneMinute, Time("2026-11-10T06:00:00Z"), Time("2026-11-10T12:30:00Z"), Arg.Any<CancellationToken>());
    }

    [Test]
    public void Pair_launched_after_its_last_closed_data_loads_nothing()
    {
        Backfill(new MarketPair(Eth, Listed: true, Time("2026-11-10T13:00:00Z")), days: 90).ShouldBeTrue();

        _exchange.ReceivedCalls().ShouldBeEmpty();
    }

    [Test]
    public void Spot_pair_loads_only_its_minutes()
    {
        Backfill(new MarketPair(Eth, Listed: true, ListedAt: null), days: 1);

        _exchange.Received().GetCandlesAsync(Eth, Interval.OneMinute, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        _exchange.DidNotReceiveWithAnyArgs().GetOpenInterestAsync(default!, from: default, to: default, cancellationToken: default);
        _exchange.DidNotReceiveWithAnyArgs().GetFundingAsync(default!, from: default, to: default, cancellationToken: default);
    }

    // Scenario «Повторный запуск»: nothing stored is asked for again.
    [Test]
    public void Window_already_covered_asks_the_exchange_nothing()
    {
        _store.GetCoverageAsync(Arg.Any<SeriesKey>(), Arg.Any<CancellationToken>()).Returns(Coverage.Empty.Add(new TimeRange(Now.AddDays(-5), Now)));

        Backfill(new MarketPair(Btc, Listed: true, ListedAt: null), days: 1).ShouldBeTrue();
        _exchange.ReceivedCalls().ShouldBeEmpty();
    }

    // Scenario «Bybit ответил 403»: the load stops until the pause ends; the caller tries the pair again later.
    [Test]
    public void Exchange_that_cannot_answer_stops_the_load_of_the_pair()
    {
        _exchange.GetCandlesAsync(default!, interval: default, from: default, to: default, cancellationToken: default)
            .ThrowsAsyncForAnyArgs(new ExchangeRateLimitedException("403"));

        Backfill(new MarketPair(Btc, Listed: true, ListedAt: null), days: 3).ShouldBeFalse();
        _exchange.ReceivedCalls().Count().ShouldBe(1);
    }

    private bool Backfill(MarketPair pair, int days)
    {
        return _handler.Handle(new BackfillPairCommand(pair, days), CancellationToken.None).GetAwaiter().GetResult();
    }

    private static DateTimeOffset Time(string text)
    {
        return DateTimeOffset.Parse(text, CultureInfo.InvariantCulture);
    }
}