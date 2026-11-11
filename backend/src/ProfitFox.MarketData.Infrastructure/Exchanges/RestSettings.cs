namespace ProfitFox.MarketData.Infrastructure.Exchanges;

/// <summary>
///     The <c>MarketData:Rest</c> section: the ceiling of the REST requests of the service a second, below the limit of Bybit by IP that the
///     production shares, and the pauses after a ban of the IP (HTTP 403) and after an answer about the limit (HTTP 429 or the code 10006).
/// </summary>
internal sealed class RestSettings
{
    public int RequestsPerSecond { get; init; } = 10;

    public TimeSpan Pause { get; init; } = TimeSpan.FromMinutes(10);

    public TimeSpan LimitPause { get; init; } = TimeSpan.FromSeconds(30);
}