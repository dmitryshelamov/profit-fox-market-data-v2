using MediatR;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using ProfitFox.MarketData.Application.Commands.History.BackfillPair;
using ProfitFox.MarketData.Application.Commands.Pairs.RefreshPairs;
using ProfitFox.MarketData.Application.Ports;
using ProfitFox.MarketData.Application.Queries.History.GetHistory;
using ProfitFox.MarketData.Application.Services.Collection;
using ProfitFox.MarketData.Domain.Entities;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Infrastructure;
using ProfitFox.MarketData.Infrastructure.BackgroundServices;
using ProfitFox.MarketData.Infrastructure.Exchanges;

using Shouldly;

namespace ProfitFox.MarketData.Tests.Unit.Infrastructure;

/// <summary>The one entry point of the layer gives every port of the application and every handler of its commands and queries.</summary>
public class InfrastructureRegistrarTests
{
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:MarketStore"] = "Host=127.0.0.1;Database=none",
                    ["MarketData:Rest:RequestsPerSecond"] = "7",
                    ["MarketData:Symbols:0"] = "BTCUSDT"
                })
            .Build();
        var services = new ServiceCollection();
        InfrastructureRegistrar.Configure(configuration, services);
        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    [TearDown]
    public void TearDown()
    {
        _provider.Dispose();
    }

    [Test]
    public void Ports_of_the_application_are_given()
    {
        _provider.GetRequiredService<IExchangeMarketData>().ShouldNotBeNull();
        _provider.GetRequiredService<IMarketStore>().ShouldNotBeNull();
        _provider.GetRequiredService<IMarketPairRepository>().ShouldNotBeNull();
        _provider.GetRequiredService<TimeProvider>().ShouldBe(TimeProvider.System);
    }

    [Test]
    public void Handlers_of_the_commands_and_queries_are_given()
    {
        using IServiceScope scope = _provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IRequestHandler<RefreshPairsCommand, IReadOnlyList<MarketPair>>>().ShouldNotBeNull();
        scope.ServiceProvider.GetRequiredService<IRequestHandler<BackfillPairCommand, bool>>().ShouldNotBeNull();
        scope.ServiceProvider.GetRequiredService<IRequestHandler<GetHistoryQuery, HistoryResult>>().ShouldNotBeNull();
    }

    // Tasks 5.2–5.5: the collector runs in the host, with the streams of the exchange and its journal in the log.
    [Test]
    public void Collector_runs_in_the_host()
    {
        _provider.GetServices<IHostedService>().ShouldContain(service => service is CollectorBackgroundService);
        _provider.GetRequiredService<IMarketDataStreams>().ShouldNotBeNull();
        _provider.GetRequiredService<ICollectorJournal>().ShouldBeOfType<LoggingCollectorJournal>();
    }

    // Decision 4: the setting MarketData:Symbols narrows the pairs, as the scanner's does.
    [Test]
    public void Symbols_of_the_collector_come_from_the_settings()
    {
        _provider.GetRequiredService<CollectorSettings>().Symbols.ShouldBe(["BTCUSDT"]);
    }

    // Scenario «Загрузка истории»: the ceiling comes from the settings.
    [Test]
    public void Ceiling_of_the_requests_comes_from_the_settings()
    {
        _provider.GetRequiredService<IOptionsMonitor<RestSettings>>().CurrentValue.RequestsPerSecond.ShouldBe(7);
    }
}