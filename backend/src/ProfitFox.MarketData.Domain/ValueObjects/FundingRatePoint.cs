namespace ProfitFox.MarketData.Domain.ValueObjects;

/// <summary>A funding settlement of a perpetual: its time and rate, a fraction such as <c>0.0001</c>.</summary>
public sealed record FundingRatePoint(DateTimeOffset Time, decimal Rate);