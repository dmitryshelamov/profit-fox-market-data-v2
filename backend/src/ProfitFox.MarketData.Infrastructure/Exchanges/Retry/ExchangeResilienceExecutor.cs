using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

using ProfitFox.MarketData.Application.Exceptions;

namespace ProfitFox.MarketData.Infrastructure.Exchanges.Retry;

/// <summary>Resilience of exchange requests; pauses run on <see cref="TimeProvider" />, so tests do not wait for real time.</summary>
internal sealed class ExchangeResilienceExecutor
{
    private static readonly TimeSpan[] RetryDelays =
        [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(16)];

    private readonly ResiliencePipeline _circuitBreaker;
    private readonly ResiliencePipeline _retry;

    public int RetryCount => RetryDelays.Length;

    public ExchangeResilienceExecutor(TimeProvider time)
    {
        // Counts every attempt; one operation makes at most 6, so it alone never opens the circuit.
        _circuitBreaker = new ResiliencePipelineBuilder { TimeProvider = time }
            .AddCircuitBreaker(
                new CircuitBreakerStrategyOptions
                {
                    FailureRatio = 0.5,
                    MinimumThroughput = 10,
                    SamplingDuration = TimeSpan.FromSeconds(60),
                    BreakDuration = TimeSpan.FromSeconds(15),
                    ShouldHandle = new PredicateBuilder().Handle<TransientExchangeException>()
                })
            .Build();
        _retry = new ResiliencePipelineBuilder { TimeProvider = time }
            .AddRetry(
                new RetryStrategyOptions
                {
                    MaxRetryAttempts = RetryDelays.Length,
                    DelayGenerator = arguments => ValueTask.FromResult<TimeSpan?>(RetryDelays[arguments.AttemptNumber]),
                    ShouldHandle = new PredicateBuilder().Handle<TransientExchangeException>()
                })
            .Build();
    }

    /// <summary>A single attempt through the circuit breaker.</summary>
    public ValueTask<T> ExecuteOnceAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken cancellationToken)
    {
        return ThroughCircuitBreakerAsync(action, cancellationToken);
    }

    /// <summary>Retries a <see cref="TransientExchangeException" /> after 1, 2, 4, 8 and 16 s; the last one propagates.</summary>
    public ValueTask<T> ExecuteWithRetriesAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken cancellationToken)
    {
        return _retry.ExecuteAsync(token => ThroughCircuitBreakerAsync(action, token), cancellationToken);
    }

    private async ValueTask<T> ThroughCircuitBreakerAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken cancellationToken)
    {
        try
        {
            return await _circuitBreaker.ExecuteAsync(action, cancellationToken);
        }
        catch (BrokenCircuitException)
        {
            throw new ExchangeUnavailableException("The exchange is temporarily unavailable after repeated failures.");
        }
    }
}