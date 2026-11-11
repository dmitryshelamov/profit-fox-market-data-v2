namespace ProfitFox.MarketData.Application.Ports;

/// <summary>The journal of the collector: what an operator needs to see in the log.</summary>
public interface ICollectorJournal
{
    /// <summary>A step of the collector failed; the collector goes on and tries it again.</summary>
    void Failed(string step, Exception exception);
}
