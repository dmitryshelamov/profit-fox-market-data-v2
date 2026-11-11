namespace ProfitFox.MarketData.Domain.ValueObjects;

/// <summary>The open interest of a linear pair at the start of a 5 minute step, in coins of the contract as the exchange reports it.</summary>
public sealed record OpenInterestPoint(DateTimeOffset Time, decimal Value);