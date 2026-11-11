using Bybit.Net;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using ProfitFox.MarketData.Application.Commands.Pairs.RefreshPairs;
using ProfitFox.MarketData.Application.Ports;
using ProfitFox.MarketData.Infrastructure.Exchanges;
using ProfitFox.MarketData.Infrastructure.Exchanges.Bybit;
using ProfitFox.MarketData.Infrastructure.Exchanges.Retry;
using ProfitFox.MarketData.Infrastructure.Persistence.Postgres;

namespace ProfitFox.MarketData.Infrastructure;

/// <summary>The one entry point of the layer: the handlers of the application, its database and the public REST data of Bybit.</summary>
public static class InfrastructureRegistrar
{
    public static void Configure(IConfiguration configuration, IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddMediatR(mediatr => mediatr.RegisterServicesFromAssemblyContaining<RefreshPairsCommand>());
        PostgresRegistrar.Configure(configuration, services);

        BybitSettings bybit = configuration.GetSection("Bybit").Get<BybitSettings>() ?? new BybitSettings();
        services.AddBybit(options =>
        {
            if (!string.IsNullOrEmpty(bybit.RestAddress))
            {
                options.Rest.Environment = BybitEnvironment.CreateCustom("market-data", bybit.RestAddress, BybitEnvironment.Live.SocketBaseAddress);
            }
        });

        // Only the gate follows a change of the settings while the service runs.
        services.Configure<RestSettings>(configuration.GetSection("MarketData:Rest"));
        services.AddSingleton<ExchangeResilienceExecutor>();
        services.AddSingleton<RestGate>();
        services.AddSingleton<IExchangeMarketData, BybitMarketData>();
    }
}