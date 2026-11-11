using System.Globalization;

using Microsoft.Extensions.Time.Testing;

using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Infrastructure.Exchanges.Retry;

using Shouldly;

namespace ProfitFox.MarketData.Tests.Unit.Infrastructure.Exchanges.Retry;

public class ExchangeResilienceExecutorTests
{
    private FakeTimeProvider _time = null!;
    private ExchangeResilienceExecutor _executor = null!;
    private int _calls;

    [SetUp]
    public void SetUp()
    {
        _time = new FakeTimeProvider(DateTimeOffset.Parse("2024-06-01T00:00:00Z", CultureInfo.InvariantCulture));
        _executor = new ExchangeResilienceExecutor(_time);
        _calls = 0;
    }

    [Test]
    public async Task Circuit_opens_after_ten_transient_failures_and_rejects_without_a_call()
    {
        await FailAsync(times: 10, () => new TransientExchangeException("HTTP 503"));

        await Should.ThrowAsync<ExchangeUnavailableException>(() => CallAsync().AsTask());
        _calls.ShouldBe(10);
    }

    [Test]
    public async Task Open_circuit_lets_a_call_through_after_fifteen_seconds()
    {
        await FailAsync(times: 10, () => new TransientExchangeException("HTTP 503"));

        _time.Advance(TimeSpan.FromSeconds(15));

        (await CallAsync()).ShouldBe(42);
        _calls.ShouldBe(11);
    }

    [Test]
    public async Task Non_transient_failures_do_not_open_circuit()
    {
        await FailAsync(times: 10, () => new ExchangeException("params error"));

        (await CallAsync()).ShouldBe(42);
        _calls.ShouldBe(11);
    }

    private async Task FailAsync(int times, Func<Exception> failure)
    {
        for (var i = 0; i < times; i++)
        {
            await Should.ThrowAsync<Exception>(() => _executor.ExecuteOnceAsync<int>(
                    _ =>
                    {
                        _calls++;

                        throw failure();
                    },
                    CancellationToken.None)
                .AsTask());
        }
    }

    private ValueTask<int> CallAsync()
    {
        return _executor.ExecuteOnceAsync(
            _ =>
            {
                _calls++;

                return ValueTask.FromResult(42);
            },
            CancellationToken.None);
    }
}