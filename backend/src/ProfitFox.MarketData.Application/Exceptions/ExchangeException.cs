namespace ProfitFox.MarketData.Application.Exceptions;

/// <summary>The exchange rejected the request, such as for a symbol it does not know; trying again gives the same answer.</summary>
public sealed class ExchangeException(string message) : Exception(message);