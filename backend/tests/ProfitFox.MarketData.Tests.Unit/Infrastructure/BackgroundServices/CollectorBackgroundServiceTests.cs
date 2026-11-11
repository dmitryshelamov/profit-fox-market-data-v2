using MediatR;

using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitFox.MarketData.Application.Commands.Pairs.RefreshPairs;
using ProfitFox.MarketData.Application.Ports;
using ProfitFox.MarketData.Application.Services.Collection;
using ProfitFox.MarketData.Application.Services.Forming;
using ProfitFox.MarketData.Domain.Entities;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Infrastructure.BackgroundServices;

namespace ProfitFox.MarketData.Tests.Unit.Infrastructure.BackgroundServices;

/// <summary>The collector runs as long as the host.</summary>
public class CollectorBackgroundServiceTests
{
    [Test]
    public async Task Host_start_starts_the_collector()
    {
        var time = new FakeTimeProvider();
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<RefreshPairsCommand>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<MarketPair>>([]));
        var streams = Substitute.For<IMarketDataStreams>();
        var store = Substitute.For<IMarketStore>();
        var journal = Substitute.For<ICollectorJournal>();
        var writer = new LiveWriterService(store, new TradeMinutesService(time), new FormingBoardService(), journal, time);
        using var service = new CollectorBackgroundService(
            new CollectorSessionService(mediator, streams, writer, Substitute.For<IExchangeMarketData>(), journal, new CollectorSettings(), time));

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        await mediator.Received(1).Send(Arg.Any<RefreshPairsCommand>(), Arg.Any<CancellationToken>());
    }
}
