namespace ProfitFox.MarketData.Application.Exceptions;

/// <summary>The exchange said the service sends too many requests, or the pause after such an answer still lasts.</summary>
public sealed class ExchangeRateLimitedException(string message) : Exception(message);