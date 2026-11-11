using Microsoft.Extensions.Logging;

using ProfitFox.MarketData.Application.Ports;

namespace ProfitFox.MarketData.Infrastructure.BackgroundServices;

/// <summary>The collector in the service log.</summary>
internal sealed class LoggingCollectorJournal(ILogger<LoggingCollectorJournal> logger) : ICollectorJournal
{
    public void Failed(string step, Exception exception)
    {
        logger.LogError(exception, "The collector step {Step} failed; it is tried again.", step);
    }
}
