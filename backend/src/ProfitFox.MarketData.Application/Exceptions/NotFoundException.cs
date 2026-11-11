namespace ProfitFox.MarketData.Application.Exceptions;

/// <summary>The thing asked for does not exist.</summary>
public sealed class NotFoundException(string message) : Exception(message);
