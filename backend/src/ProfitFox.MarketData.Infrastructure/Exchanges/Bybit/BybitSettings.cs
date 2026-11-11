namespace ProfitFox.MarketData.Infrastructure.Exchanges.Bybit;

/// <summary>The <c>Bybit</c> section: where the service reaches Bybit.</summary>
/// <param name="RestAddress">The address REST calls go to instead of the live Bybit, such as <c>http://127.0.0.1:5296</c>; without it, the live Bybit.</param>
internal sealed record BybitSettings(string? RestAddress = null);