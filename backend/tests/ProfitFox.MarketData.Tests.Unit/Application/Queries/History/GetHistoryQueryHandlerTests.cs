using System.Globalization;

using Microsoft.Extensions.Time.Testing;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Application.Queries.History.GetHistory;
using ProfitFox.MarketData.Application.Ports;
using ProfitFox.MarketData.Domain.Entities;
using ProfitFox.MarketData.Domain.Entities.Enums;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects;

using Shouldly;

namespace ProfitFox.MarketData.Tests.Unit.Application.Queries.History;

/// <summary>Requirements «Чтение истории» and «Догрузка при чтении истории».</summary>
public class GetHistoryQueryHandlerTests
{
    private static readonly DateTimeOffset Now = Time("2026-11-10T12:30:00Z");
    private static readonly Instrument Eth = new Instrument(MarketCategory.Linear, "ETHUSDT");
    private static readonly SeriesKey Hours = SeriesKey.Candles(Eth, Interval.OneHour);

    private IExchangeMarketData _exchange = null!;
    private IMarketStore _store = null!;
    private IMarketPairRepository _pairs = null!;
    private GetHistoryQueryHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _exchange = Substitute.For<IExchangeMarketData>();
        _store = Substitute.For<IMarketStore>();
        _pairs = Substitute.For<IMarketPairRepository>();
        _pairs.FindAsync(Eth, Arg.Any<CancellationToken>()).Returns(new MarketPair(Eth, true, null));
        _store.GetCoverageAsync(Arg.Any<SeriesKey>(), Arg.Any<CancellationToken>()).Returns(Coverage.Empty);
        _store.ReadAsync(Arg.Any<SeriesKey>(), Arg.Any<TimeRange>(), Arg.Any<CancellationToken>()).Returns(SeriesRows.Empty);
        _handler = new GetHistoryQueryHandler(_exchange, _store, _pairs, new FakeTimeProvider(Now));
    }

    // Scenario «Период внутри покрытия».
    [Test]
    public void Period_inside_the_coverage_is_read_without_the_exchange()
    {
        var period = new TimeRange(Time("2026-11-09T00:00:00Z"), Time("2026-11-10T00:00:00Z"));
        var minutes = SeriesKey.Candles(Eth, Interval.OneMinute);
        SeriesRows stored = SeriesRows.OfCandles([Candle(period.From)]);
        _store.GetCoverageAsync(minutes, Arg.Any<CancellationToken>()).Returns(Coverage.Empty.Add(new TimeRange(period.From.AddDays(-1), period.To.AddDays(1))));
        _store.ReadAsync(minutes, period, Arg.Any<CancellationToken>()).Returns(stored);

        HistoryResult result = Get(minutes, period);

        result.Rows.ShouldBe(stored);
        result.Coverage.ShouldBe([period]);
        result.ExchangeUnavailable.ShouldBeFalse();
        _exchange.ReceivedCalls().ShouldBeEmpty();
    }

    // Scenario «Неизвестная пара».
    [Test]
    public void Unknown_pair_is_not_found_and_nothing_is_loaded()
    {
        var unknown = new Instrument(MarketCategory.Spot, "NOPEUSDT");

        Should.Throw<NotFoundException>(() => Get(SeriesKey.Candles(unknown, Interval.OneHour), Day("2026-04-01")));
        _exchange.ReceivedCalls().ShouldBeEmpty();
    }

    // Scenario «Свечи старше окна»: loaded, stored with their coverage, then read from storage.
    [Test]
    public void Candles_outside_the_coverage_are_loaded_and_stored_before_they_are_read()
    {
        TimeRange period = Day("2026-04-01");
        Candle[] loaded = [Candle(period.From), Candle(period.From.AddHours(1))];
        SeriesRows stored = SeriesRows.OfCandles(loaded);
        _exchange.GetCandlesAsync(Eth, Interval.OneHour, period.From, period.To, Arg.Any<CancellationToken>()).Returns(loaded);
        _store.GetCoverageAsync(Hours, Arg.Any<CancellationToken>()).Returns(Coverage.Empty, Coverage.Empty.Add(period));
        _store.ReadAsync(Hours, period, Arg.Any<CancellationToken>()).Returns(stored);

        HistoryResult result = Get(Hours, period);

        _store.Received(1).ReplaceAsync(Hours, period, Arg.Is<SeriesRows>(rows => rows!.Candles.SequenceEqual(loaded)), Arg.Any<CancellationToken>());
        result.Rows.ShouldBe(stored);
        result.Coverage.ShouldBe([period]);
    }

    [Test]
    public void Only_the_missing_parts_of_the_period_are_loaded()
    {
        TimeRange period = Day("2026-04-01");
        var covered = new TimeRange(period.From.AddHours(6), period.From.AddHours(18));
        _store.GetCoverageAsync(Hours, Arg.Any<CancellationToken>()).Returns(Coverage.Empty.Add(covered));
        _exchange.GetCandlesAsync(default!, default, default, default, default).ReturnsForAnyArgs([]);

        Get(Hours, period);

        _exchange.Received(1).GetCandlesAsync(Eth, Interval.OneHour, period.From, covered.From, Arg.Any<CancellationToken>());
        _exchange.Received(1).GetCandlesAsync(Eth, Interval.OneHour, covered.To, period.To, Arg.Any<CancellationToken>());
        _exchange.ReceivedCalls().Count().ShouldBe(2);
    }

    // A page of the exchange holds at most 1000 candles: a longer period goes page by page from new to old, each page stored as it comes.
    [Test]
    public void Long_period_is_loaded_in_pages_of_a_thousand_candles_from_new_to_old()
    {
        var period = new TimeRange(Time("2026-01-01T00:00:00Z"), Time("2026-01-01T00:00:00Z").AddHours(1500));
        DateTimeOffset split = period.To.AddHours(-1000);
        _exchange.GetCandlesAsync(default!, default, default, default, default).ReturnsForAnyArgs([]);

        Get(Hours, period);

        Received.InOrder(() =>
        {
            _exchange.GetCandlesAsync(Eth, Interval.OneHour, split, period.To, Arg.Any<CancellationToken>());
            _store.ReplaceAsync(Hours, new TimeRange(split, period.To), Arg.Any<SeriesRows>(), Arg.Any<CancellationToken>());
            _exchange.GetCandlesAsync(Eth, Interval.OneHour, period.From, split, Arg.Any<CancellationToken>());
            _store.ReplaceAsync(Hours, new TimeRange(period.From, split), Arg.Any<SeriesRows>(), Arg.Any<CancellationToken>());
        });
    }

    // The candle that is still forming is never loaded: its period stays outside the coverage.
    [Test]
    public void Candles_are_loaded_up_to_the_start_of_the_forming_one()
    {
        var period = new TimeRange(Time("2026-11-10T10:00:00Z"), Time("2026-11-10T14:00:00Z"));
        _exchange.GetCandlesAsync(default!, default, default, default, default).ReturnsForAnyArgs([]);

        Get(Hours, period);

        _exchange.Received(1).GetCandlesAsync(Eth, Interval.OneHour, period.From, Time("2026-11-10T12:00:00Z"), Arg.Any<CancellationToken>());
    }

    [Test]
    public void Period_of_the_future_loads_nothing()
    {
        Get(Hours, Day("2026-11-12"));

        _exchange.ReceivedCalls().ShouldBeEmpty();
    }

    // Scenario «Ликвидации до начала сбора»: the exchange has no history of them.
    [Test]
    public void Liquidations_outside_the_coverage_are_not_loaded()
    {
        HistoryResult result = Get(SeriesKey.Of(DataKind.Liquidations, Eth), Day("2026-04-01"));

        result.Rows.ShouldBe(SeriesRows.Empty);
        result.Coverage.ShouldBeEmpty();
        result.ExchangeUnavailable.ShouldBeFalse();
        _exchange.ReceivedCalls().ShouldBeEmpty();
        _store.DidNotReceiveWithAnyArgs().ReplaceAsync(default!, default!, default!, default);
    }

    [Test]
    public void Open_interest_is_loaded_in_one_request_up_to_the_current_step()
    {
        var period = new TimeRange(Time("2026-11-10T12:00:00Z"), Time("2026-11-10T13:00:00Z"));
        var series = SeriesKey.Of(DataKind.OpenInterest, Eth);
        OpenInterestPoint[] points = [new OpenInterestPoint(period.From, 10m)];
        _exchange.GetOpenInterestAsync(Eth, period.From, Time("2026-11-10T12:30:00Z"), Arg.Any<CancellationToken>()).Returns(points);
        var handler = new GetHistoryQueryHandler(_exchange, _store, _pairs, new FakeTimeProvider(Time("2026-11-10T12:34:10Z")));

        handler.Handle(new GetHistoryQuery(series, period), CancellationToken.None).GetAwaiter().GetResult();

        _store.Received(1).ReplaceAsync(
            series,
            new TimeRange(period.From, Time("2026-11-10T12:30:00Z")),
            Arg.Is<SeriesRows>(rows => rows!.OpenInterest.SequenceEqual(points)),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public void Funding_is_loaded_in_one_request_up_to_now()
    {
        var period = new TimeRange(Time("2026-11-10T00:00:00Z"), Time("2026-11-11T00:00:00Z"));
        var series = SeriesKey.Of(DataKind.Funding, Eth);
        FundingRatePoint[] rates = [new FundingRatePoint(Time("2026-11-10T08:00:00Z"), 0.0001m)];
        _exchange.GetFundingAsync(Eth, period.From, Now, Arg.Any<CancellationToken>()).Returns(rates);

        Get(series, period);

        _store.Received(1).ReplaceAsync(series, new TimeRange(period.From, Now), Arg.Is<SeriesRows>(rows => rows!.Funding.SequenceEqual(rates)), Arg.Any<CancellationToken>());
    }

    // Scenario «Биржа не отвечает»: what storage holds comes back with the mark, without waiting for the exchange.
    [TestCase(false)]
    [TestCase(true)]
    public void Exchange_that_cannot_answer_leaves_the_period_as_it_is_with_the_mark(bool rateLimited)
    {
        TimeRange period = Day("2026-04-01");
        Exception failure = rateLimited ? new ExchangeRateLimitedException("limit") : new ExchangeUnavailableException("down");
        _exchange.GetCandlesAsync(default!, default, default, default, default).ThrowsAsyncForAnyArgs(failure);

        HistoryResult result = Get(Hours, period);

        result.ExchangeUnavailable.ShouldBeTrue();
        result.Coverage.ShouldBeEmpty();
        _store.DidNotReceiveWithAnyArgs().ReplaceAsync(default!, default!, default!, default);
    }

    [Test]
    public void Exchange_that_cannot_answer_is_not_asked_for_the_rest_of_the_period()
    {
        TimeRange period = Day("2026-04-01");
        var covered = new TimeRange(period.From.AddHours(6), period.From.AddHours(18));
        _store.GetCoverageAsync(Hours, Arg.Any<CancellationToken>()).Returns(Coverage.Empty.Add(covered));
        _exchange.GetCandlesAsync(default!, default, default, default, default).ThrowsAsyncForAnyArgs(new ExchangeUnavailableException("down"));

        Get(Hours, period);

        _exchange.ReceivedCalls().Count().ShouldBe(1);
    }

    private HistoryResult Get(SeriesKey series, TimeRange period)
    {
        return _handler.Handle(new GetHistoryQuery(series, period), CancellationToken.None).GetAwaiter().GetResult();
    }

    private static Candle Candle(DateTimeOffset openTime)
    {
        return new Candle(openTime, 100m, 110m, 90m, 105m, 3m, 315m);
    }

    private static TimeRange Day(string day)
    {
        DateTimeOffset start = Time(day + "T00:00:00Z");

        return new TimeRange(start, start.AddDays(1));
    }

    private static DateTimeOffset Time(string text)
    {
        return DateTimeOffset.Parse(text, CultureInfo.InvariantCulture);
    }
}
