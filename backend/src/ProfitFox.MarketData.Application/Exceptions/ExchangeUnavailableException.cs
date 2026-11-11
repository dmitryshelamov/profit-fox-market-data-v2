namespace ProfitFox.MarketData.Application.Exceptions;

/// <summary>The exchange cannot answer for now: it is down, times out or failed in a way a later try may not.</summary>
public sealed class ExchangeUnavailableException(string message) : Exception(message);
