using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using ProfitFox.MarketData.Application.Ports;
using ProfitFox.MarketData.Application.Services.Collection;
using ProfitFox.MarketData.Application.Services.Forming;
using ProfitFox.MarketData.Infrastructure.BackgroundServices;
using ProfitFox.MarketData.Infrastructure.Exchanges;
using ProfitFox.MarketData.Infrastructure.Exchanges.Bybit;
using ProfitFox.MarketData.Infrastructure.Exchanges.Retry;
using ProfitFox.MarketData.Infrastructure.Mediatr;
using ProfitFox.MarketData.Infrastructure.Persistence.Postgres;

namespace ProfitFox.MarketData.Infrastructure;

public static class InfrastructureRegistrar
{
    /// <summary>
    ///     The services of the market data service: the handlers of the application, its own database, the public REST data and streams of
    ///     Bybit and the collector that runs in the host.
    /// </summary>
    public static void Configure(IConfiguration configuration, IServiceCollection services)
    {
        ConfigureCommon(configuration, services);
    }

    private static void ConfigureCommon(IConfiguration configuration, IServiceCollection services)
    {
        MediatrRegistrar.Configure(services);
        PostgresRegistrar.Configure(configuration, services);

        services.AddSingleton(TimeProvider.System);
        BybitSettings bybit = configuration.GetSection("Bybit").Get<BybitSettings>() ?? new BybitSettings();
        services.AddBybit(options => options.Rest.Environment = bybit.Environment());

        // Only the gate follows a change of the settings while the service runs.
        services.Configure<RestSettings>(configuration.GetSection("MarketData:Rest"));
        services.AddSingleton<ExchangeResilienceExecutor>();
        services.AddSingleton<RestGate>();
        services.AddSingleton<IExchangeMarketData, BybitMarketData>();
        services.AddSingleton(bybit);
        services.AddSingleton<IMarketDataStreams, BybitMarketDataStreams>();

        services.AddSingleton(configuration.GetSection("MarketData").Get<CollectorSettings>() ?? new CollectorSettings());
        services.AddSingleton<ICollectorJournal, LoggingCollectorJournal>();
        services.AddSingleton<TradeMinutesService>();
        services.AddSingleton<FormingBoardService>();
        services.AddSingleton<LiveWriterService>();
        services.AddSingleton<CollectorSessionService>();
        services.AddHostedService<CollectorBackgroundService>();
    }
}
