using ProfitFox.MarketData.Application.Exceptions;

namespace ProfitFox.MarketData.Infrastructure.Exchanges.Retry;

/// <summary>
///     An exchange failure worth retrying: rate limit, server error, timeout or no connection. <see cref="RateLimited" /> says the
///     exchange said the requests went over its rate limit or banned the IP for it; <see cref="IpBanned" /> says it banned the IP.
/// </summary>
internal sealed class TransientExchangeException(string message, bool rateLimited = false, bool ipBanned = false) : Exception(message)
{
    public bool RateLimited { get; } = rateLimited;

    public bool IpBanned { get; } = ipBanned;

    /// <summary>What the caller is told once no more attempts are made.</summary>
    public Exception Unavailable(string message)
    {
        return RateLimited ? new ExchangeRateLimitedException(message) : new ExchangeUnavailableException(message);
    }
}