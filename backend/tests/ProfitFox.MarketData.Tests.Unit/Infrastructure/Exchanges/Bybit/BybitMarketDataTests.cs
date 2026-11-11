using System.Globalization;

using Bybit.Net.Clients;
using Bybit.Net.Objects.Options;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Domain.Entities;
using ProfitFox.MarketData.Domain.Entities.Enums;
using ProfitFox.MarketData.Domain.ValueObjects;
using ProfitFox.MarketData.Infrastructure.Exchanges;
using ProfitFox.MarketData.Infrastructure.Exchanges.Bybit;
using ProfitFox.MarketData.Infrastructure.Exchanges.Retry;
using ProfitFox.MarketData.Tests.Shared.Bybit;

using Shouldly;

namespace ProfitFox.MarketData.Tests.Unit.Infrastructure.Exchanges.Bybit;

/// <summary>The public REST data of Bybit v5 the service loads its history from, through the fake Bybit.</summary>
public class BybitMarketDataTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2024-03-01T00:00:00Z", CultureInfo.InvariantCulture);
    private static readonly Instrument Sol = new Instrument(MarketCategory.Linear, "SOLUSDT");

    private FakeBybitHandler _bybit = null!;
    private IRestPauseStore _pauses = null!;
    private RestGate _gate = null!;
    private BybitMarketData _client = null!;

    [SetUp]
    public void SetUp()
    {
        _bybit = new FakeBybitHandler();
        _pauses = Substitute.For<IRestPauseStore>();
        _pauses.GetPausedUntilAsync(Arg.Any<CancellationToken>()).Returns((DateTimeOffset?)null);
        var settings = Substitute.For<IOptionsMonitor<RestSettings>>();
        settings.CurrentValue.Returns(new RestSettings { RequestsPerSecond = 1000 });
        var time = new FakeTimeProvider(Start);
        _gate = new RestGate(settings, new ExchangeResilienceExecutor(time), _pauses, time);
        _client = new BybitMarketData(
            new BybitRestClient(
                new HttpClient(_bybit, disposeHandler: false),
                NullLoggerFactory.Instance,
                Options.Create(new BybitRestOptions { RateLimiterEnabled = false })),
            _gate);
    }

    [TearDown]
    public void TearDown()
    {
        _gate.Dispose();
        _bybit.Dispose();
    }

    [Test]
    public async Task Spot_pairs_are_listed_with_their_quote_coin_and_trading_without_a_launch()
    {
        _bybit.Instruments.Add(("spot", "ETHUSDT"));
        _bybit.Coins[("spot", "ETHUSDT")] = ("ETH", "USDT");
        _bybit.Instruments.Add(("spot", "OLDUSDT"));
        _bybit.Coins[("spot", "OLDUSDT")] = ("OLD", "USDT");
        _bybit.Statuses[("spot", "OLDUSDT")] = "Closed";

        IReadOnlyList<ListedInstrument> listed = await _client.ListInstrumentsAsync(MarketCategory.Spot, CancellationToken.None);

        listed.ShouldBe([new ListedInstrument(MarketCategory.Spot, "ETHUSDT", "USDT", Trading: true), new ListedInstrument(MarketCategory.Spot, "OLDUSDT", "USDT")]);
    }

    [Test]
    public async Task Linear_pairs_are_listed_page_by_page_with_their_contract_and_launch()
    {
        DateTimeOffset launch = Time("2026-10-01T08:00:00Z");
        _bybit.LinearPageSize = 1;
        _bybit.Instruments.Add(("linear", "BTCUSDT"));
        _bybit.Coins[("linear", "BTCUSDT")] = ("BTC", "USDT");
        _bybit.LaunchTimes["BTCUSDT"] = launch;
        _bybit.Instruments.Add(("linear", "BTCUSDT-26DEC26"));
        _bybit.Coins[("linear", "BTCUSDT-26DEC26")] = ("BTC", "USDT");
        _bybit.ContractTypes["BTCUSDT-26DEC26"] = "LinearFutures";
        _bybit.LaunchTimes["BTCUSDT-26DEC26"] = launch;

        IReadOnlyList<ListedInstrument> listed = await _client.ListInstrumentsAsync(MarketCategory.Linear, CancellationToken.None);

        listed.ShouldBe(
        [
            new ListedInstrument(MarketCategory.Linear, "BTCUSDT", "USDT", Trading: true, Perpetual: true, launch),
            new ListedInstrument(MarketCategory.Linear, "BTCUSDT-26DEC26", "USDT", Trading: true, ListedAt: launch)
        ]);
        _bybit.ListRequests.Count.ShouldBe(2);
    }

    [Test]
    public async Task Candles_of_the_period_come_exactly_oldest_first_without_the_one_at_its_end()
    {
        _bybit.Klines.Add(["2024-03-01T00:00:00Z", "100.5", "101.25", "99.75", "100.125", "12.5", "1250.75"]);
        _bybit.Klines.Add(["2024-03-01T00:01:00Z", "100.125", "102", "100", "101", "7", "700"]);
        _bybit.Klines.Add(["2024-03-01T00:02:00Z", "1", "1", "1", "1", "1", "1"]);

        IReadOnlyList<Candle> candles = await _client.GetCandlesAsync(Sol, Interval.OneMinute, Start, Start.AddMinutes(2), CancellationToken.None);

        candles.ShouldBe(
        [
            new Candle(Start, Open: 100.5m, High: 101.25m, Low: 99.75m, Close: 100.125m, Volume: 12.5m, Turnover: 1250.75m),
            new Candle(Start.AddMinutes(1), Open: 100.125m, High: 102m, Low: 100m, Close: 101m, Volume: 7m, Turnover: 700m)
        ]);
        _bybit.KlineRequests.Count.ShouldBe(1);
    }

    // A page of Bybit holds at most 1000 candles: a longer period goes page by page from new to old.
    [Test]
    public async Task Long_period_is_read_in_pages_of_a_thousand_from_new_to_old()
    {
        _bybit.Klines.AddRange(Enumerable.Range(start: 0, count: 2500).Select(hour => new[] { Start.AddHours(hour).ToString("O"), "1", "1", "1", "1", "1", "1" }));

        IReadOnlyList<Candle> candles = await _client.GetCandlesAsync(Sol, Interval.OneHour, Start, Start.AddHours(2500), CancellationToken.None);

        candles.Count.ShouldBe(2500);
        candles.Select(candle => candle.OpenTime).ShouldBeInOrder();
        _bybit.KlineRequests.Select(request => request.Start).ShouldBe([Start.AddHours(1500), Start.AddHours(500), Start]);
    }

    [Test]
    public async Task Open_interest_follows_the_cursor_through_every_page_without_the_point_at_the_end()
    {
        _bybit.OpenInterestPageSize = 2;
        _bybit.OpenInterest.AddRange(Enumerable.Range(start: 0, count: 6).Select(i => ("SOLUSDT", Start.AddMinutes(5 * i), $"1000.{i}5")));

        IReadOnlyList<OpenInterestPoint> points = await _client.GetOpenInterestAsync(Sol, Start, Start.AddMinutes(25), CancellationToken.None);

        points.ShouldBe(
        [
            .. Enumerable.Range(start: 0, count: 5).Select(i => new OpenInterestPoint(Start.AddMinutes(5 * i), decimal.Parse($"1000.{i}5", CultureInfo.InvariantCulture)))
        ]);
        _bybit.OpenInterestRequests.Count.ShouldBe(3);
        _bybit.OpenInterestRequests.ShouldAllBe(query => query.Contains("intervalTime=5min") && query.Contains("limit=200"));
    }

    [Test]
    public async Task Funding_pages_back_in_time_through_the_period()
    {
        _bybit.FundingRates.AddRange(Enumerable.Range(start: 0, count: 450).Select(i => ("SOLUSDT", Start.AddHours(8 * i), $"0.0001{i:000}")));

        IReadOnlyList<FundingRatePoint> rates = await _client.GetFundingAsync(Sol, Start, Start.AddHours(8 * 450), CancellationToken.None);

        rates.Count.ShouldBe(450);
        rates[0].ShouldBe(new FundingRatePoint(Start, Rate: 0.0001000m));
        rates[^1].ShouldBe(new FundingRatePoint(Start.AddHours(8 * 449), Rate: 0.0001449m));
        rates.Select(rate => rate.Time).ShouldBeInOrder();
        _bybit.FundingRequests.Count.ShouldBe(3);
    }

    // Scenario «Bybit ответил 403» and the short pause of 429 and 10006.
    [TestCase("403", arg2: 600)]
    [TestCase("429", arg2: 30)]
    [TestCase("10006", arg2: 30)]
    public async Task Answer_about_the_limit_pauses_the_requests(string answer, int seconds)
    {
        _bybit.RateLimit = answer;

        await Should.ThrowAsync<ExchangeRateLimitedException>(() => _client.GetCandlesAsync(Sol, Interval.OneMinute, Start, Start.AddMinutes(1), CancellationToken.None));

        await _pauses.Received(1).PauseAsync(Start.AddSeconds(seconds), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Rejected_request_is_not_retried()
    {
        _bybit.RejectKlines = true;

        await Should.ThrowAsync<ExchangeException>(() => _client.GetCandlesAsync(Sol, Interval.OneMinute, Start, Start.AddMinutes(1), CancellationToken.None));

        _bybit.Requests.ShouldBe(1);
    }

    private static DateTimeOffset Time(string text)
    {
        return DateTimeOffset.Parse(text, CultureInfo.InvariantCulture);
    }
}