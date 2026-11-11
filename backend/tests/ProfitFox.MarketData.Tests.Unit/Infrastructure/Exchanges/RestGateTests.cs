using System.Globalization;

using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Infrastructure.Exchanges;
using ProfitFox.MarketData.Infrastructure.Exchanges.Retry;

using Shouldly;

namespace ProfitFox.MarketData.Tests.Unit.Infrastructure.Exchanges;

/// <summary>Requirements «Потолок REST-запросов» and «Паузы по ответам о лимите»: every REST request of the service goes through the gate.</summary>
public class RestGateTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2024-03-10T12:00:00Z", CultureInfo.InvariantCulture);

    private FakeTimeProvider _time = null!;
    private IRestPauseStore _state = null!;

    [SetUp]
    public void SetUp()
    {
        _time = new FakeTimeProvider(Start);
        _state = Substitute.For<IRestPauseStore>();
        _state.GetPausedUntilAsync(Arg.Any<CancellationToken>()).Returns((DateTimeOffset?)null);
    }

    [Test]
    public async Task Requests_go_no_faster_than_the_setting()
    {
        using RestGate gate = Gate(new RestSettings { RequestsPerSecond = 2 });
        var sent = 0;

        Task<int>[] requests =
            [.. Enumerable.Range(start: 0, count: 5).Select(_ => gate.SendAsync(_ => ValueTask.FromResult(Interlocked.Increment(ref sent)), CancellationToken.None))];
        await Task.Delay(50);
        int first = sent;
        _time.Advance(TimeSpan.FromSeconds(1));
        await Task.Delay(50);
        int second = sent;
        _time.Advance(TimeSpan.FromSeconds(1));
        await Task.WhenAll(requests);

        first.ShouldBe(2);
        second.ShouldBe(4);
        sent.ShouldBe(5);
    }

    // The limit changes while the service runs, from the settings file on the server.
    [Test]
    public async Task Changed_setting_holds_from_the_next_second()
    {
        IOptionsMonitor<RestSettings> settings = Monitor(new RestSettings { RequestsPerSecond = 1 });
        using RestGate gate = Gate(settings);
        var sent = 0;

        Task<int>[] requests =
            [.. Enumerable.Range(start: 0, count: 5).Select(_ => gate.SendAsync(_ => ValueTask.FromResult(Interlocked.Increment(ref sent)), CancellationToken.None))];
        await Task.Delay(50);
        sent.ShouldBe(1);
        settings.CurrentValue.Returns(new RestSettings { RequestsPerSecond = 3 });
        _time.Advance(TimeSpan.FromSeconds(1));
        await Task.Delay(50);
        sent.ShouldBe(4);
        _time.Advance(TimeSpan.FromSeconds(1));
        await Task.WhenAll(requests);

        sent.ShouldBe(5);
    }

    // Scenario «Bybit ответил 403».
    [Test]
    public async Task No_request_goes_until_the_pause_after_a_ban_of_the_ip_ends()
    {
        using RestGate gate = Gate(new RestSettings { Pause = TimeSpan.FromMinutes(10) });
        var sent = 0;

        await Should.ThrowAsync<ExchangeRateLimitedException>(() => gate.SendAsync<int>(
            _ => throw new TransientExchangeException("HTTP 403", rateLimited: true, ipBanned: true),
            CancellationToken.None));
        _time.Advance(TimeSpan.FromMinutes(10) - TimeSpan.FromSeconds(1));
        await Should.ThrowAsync<ExchangeRateLimitedException>(() => gate.SendAsync(_ => ValueTask.FromResult(++sent), CancellationToken.None));
        _time.Advance(TimeSpan.FromSeconds(1));
        int after = await gate.SendAsync(_ => ValueTask.FromResult(++sent), CancellationToken.None);

        after.ShouldBe(1);
        await _state.Received(1).PauseAsync(Start.AddMinutes(10), Arg.Any<CancellationToken>());
    }

    // Short pause after HTTP 429 or the code 10006.
    [Test]
    public async Task No_request_goes_until_the_short_pause_after_a_limit_ends()
    {
        using RestGate gate = Gate(new RestSettings { LimitPause = TimeSpan.FromSeconds(30) });
        var sent = 0;

        await Should.ThrowAsync<ExchangeRateLimitedException>(() => gate.SendAsync<int>(_ => throw Limit(), CancellationToken.None));
        _time.Advance(TimeSpan.FromSeconds(29));
        await Should.ThrowAsync<ExchangeRateLimitedException>(() => gate.SendAsync(_ => ValueTask.FromResult(++sent), CancellationToken.None));
        _time.Advance(TimeSpan.FromSeconds(1));
        int after = await gate.SendAsync(_ => ValueTask.FromResult(++sent), CancellationToken.None);

        after.ShouldBe(1);
        await _state.Received(1).PauseAsync(Start.AddSeconds(30), Arg.Any<CancellationToken>());
    }

    // Pauses in a row double up to the pause of a ban.
    [Test]
    public async Task Limits_in_a_row_double_the_short_pause_up_to_the_rest_pause()
    {
        using RestGate gate = Gate(new RestSettings { LimitPause = TimeSpan.FromSeconds(30), Pause = TimeSpan.FromMinutes(10) });
        List<TimeSpan> pauses = RecordPauses();

        for (var i = 0; i < 7; i++)
        {
            await Should.ThrowAsync<ExchangeRateLimitedException>(() => gate.SendAsync<int>(_ => throw Limit(), CancellationToken.None));
            _time.Advance(pauses[^1]);
        }

        pauses.ShouldBe(
        [
            TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(8),
            TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10)
        ]);
    }

    // A request that went through starts the ladder again.
    [Test]
    public async Task Request_that_went_through_starts_the_short_pause_again()
    {
        using RestGate gate = Gate(new RestSettings { LimitPause = TimeSpan.FromSeconds(30) });
        List<TimeSpan> pauses = RecordPauses();

        await Should.ThrowAsync<ExchangeRateLimitedException>(() => gate.SendAsync<int>(_ => throw Limit(), CancellationToken.None));
        _time.Advance(pauses[^1]);
        await gate.SendAsync(_ => ValueTask.FromResult(1), CancellationToken.None);
        await Should.ThrowAsync<ExchangeRateLimitedException>(() => gate.SendAsync<int>(_ => throw Limit(), CancellationToken.None));

        pauses.ShouldBe([TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30)]);
    }

    // A ban of the IP pauses for the whole RestPause however many limits came before it, and leaves the ladder where it was.
    [Test]
    public async Task Ban_of_the_ip_after_limits_pauses_for_the_rest_pause_and_keeps_the_ladder()
    {
        using RestGate gate = Gate(new RestSettings { LimitPause = TimeSpan.FromSeconds(30), Pause = TimeSpan.FromMinutes(10) });
        List<TimeSpan> pauses = RecordPauses();

        foreach (TransientExchangeException answer in new[] { Limit(), Limit(), new TransientExchangeException("HTTP 403", rateLimited: true, ipBanned: true), Limit() })
        {
            await Should.ThrowAsync<ExchangeRateLimitedException>(() => gate.SendAsync<int>(_ => throw answer, CancellationToken.None));
            _time.Advance(pauses[^1]);
        }

        pauses.ShouldBe([TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(2)]);
    }

    // Two lanes send at once: a limit on a request sent before the ban of the IP does not shorten its pause.
    [Test]
    public async Task Limit_that_comes_after_a_ban_of_the_ip_does_not_shorten_its_pause()
    {
        using RestGate gate = Gate(new RestSettings { LimitPause = TimeSpan.FromSeconds(30), Pause = TimeSpan.FromMinutes(10) });
        var answered = new TaskCompletionSource();
        Task<int> sentBefore = gate.SendAsync<int>(
            async _ =>
            {
                await answered.Task;

                throw Limit();
            },
            CancellationToken.None);

        await Should.ThrowAsync<ExchangeRateLimitedException>(() =>
            gate.SendAsync<int>(_ => throw new TransientExchangeException("HTTP 403", rateLimited: true, ipBanned: true), CancellationToken.None));
        answered.SetResult();
        await Should.ThrowAsync<ExchangeRateLimitedException>(() => sentBefore);
        _time.Advance(TimeSpan.FromSeconds(30));

        await Should.ThrowAsync<ExchangeRateLimitedException>(() => gate.SendAsync(_ => ValueTask.FromResult(1), CancellationToken.None));
    }

    // Scenario «Перезапуск во время паузы»: a pause recorded before a restart, or by the holder another process took over from, still holds.
    [Test]
    public async Task Pause_recorded_before_the_start_of_the_process_holds()
    {
        _state.GetPausedUntilAsync(Arg.Any<CancellationToken>()).Returns(Start.AddMinutes(5));
        using RestGate gate = Gate(new RestSettings());
        var sent = 0;

        await Should.ThrowAsync<ExchangeRateLimitedException>(() => gate.SendAsync(_ => ValueTask.FromResult(++sent), CancellationToken.None));
        _time.Advance(TimeSpan.FromMinutes(5));
        int after = await gate.SendAsync(_ => ValueTask.FromResult(++sent), CancellationToken.None);

        after.ShouldBe(1);
    }

    [Test]
    public async Task Pause_is_read_again_when_the_first_read_failed()
    {
        _state.GetPausedUntilAsync(Arg.Any<CancellationToken>())
            .Returns(
                Task.FromException<DateTimeOffset?>(new InvalidOperationException("database down")),
                Task.FromResult<DateTimeOffset?>(null));
        using RestGate gate = Gate(new RestSettings());

        await Should.ThrowAsync<InvalidOperationException>(() => gate.SendAsync(_ => ValueTask.FromResult(1), CancellationToken.None));

        (await gate.SendAsync(_ => ValueTask.FromResult(2), CancellationToken.None)).ShouldBe(2);
    }

    [Test]
    public async Task Other_transient_failure_is_retried_after_a_second()
    {
        using RestGate gate = Gate(new RestSettings());
        var times = new List<DateTimeOffset>();

        Task<string> request = gate.SendAsync(
            _ =>
            {
                times.Add(_time.GetUtcNow());

                return times.Count == 1 ? throw new TransientExchangeException("HTTP 502") : ValueTask.FromResult("ok");
            },
            CancellationToken.None);

        while (!request.IsCompleted)
        {
            await Task.Delay(20);
            _time.Advance(TimeSpan.FromSeconds(1));
        }

        (await request).ShouldBe("ok");
        times.ShouldBe([Start, Start.AddSeconds(1)]);
    }

    // Each pause the gate records, as its length from the moment it was recorded.
    private List<TimeSpan> RecordPauses()
    {
        var pauses = new List<TimeSpan>();
        _state.When(state => state.PauseAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()))
            .Do(call => pauses.Add(call.Arg<DateTimeOffset>() - _time.GetUtcNow()));

        return pauses;
    }

    private static TransientExchangeException Limit()
    {
        return new TransientExchangeException("10006: Too many visits!", rateLimited: true);
    }

    private RestGate Gate(RestSettings settings)
    {
        return Gate(Monitor(settings));
    }

    private RestGate Gate(IOptionsMonitor<RestSettings> settings)
    {
        return new RestGate(settings, new ExchangeResilienceExecutor(_time), _state, _time);
    }

    private static IOptionsMonitor<RestSettings> Monitor(RestSettings settings)
    {
        var monitor = Substitute.For<IOptionsMonitor<RestSettings>>();
        monitor.CurrentValue.Returns(settings);

        return monitor;
    }
}