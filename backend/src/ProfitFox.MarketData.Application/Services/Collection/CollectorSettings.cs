namespace ProfitFox.MarketData.Application.Services.Collection;

/// <summary>The settings <c>MarketData</c> of the collector.</summary>
public sealed class CollectorSettings
{
    /// <summary>Narrow the pairs to these symbols, as the scanner does in tests; empty collects every pair.</summary>
    public string[] Symbols { get; init; } = [];
}
