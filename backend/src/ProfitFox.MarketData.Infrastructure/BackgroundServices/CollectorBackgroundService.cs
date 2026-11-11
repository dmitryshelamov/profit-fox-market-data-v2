using Microsoft.Extensions.Hosting;

using ProfitFox.MarketData.Application.Services.Collection;

namespace ProfitFox.MarketData.Infrastructure.BackgroundServices;

/// <summary>The collector runs as long as the host; it tries its failed steps again by itself.</summary>
internal sealed class CollectorBackgroundService(CollectorSessionService session) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return session.RunAsync(stoppingToken);
    }
}
