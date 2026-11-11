using MediatR;

using ProfitFox.MarketData.Application.Ports;
using ProfitFox.MarketData.Application.Services.History;
using ProfitFox.MarketData.Domain.Entities;
using ProfitFox.MarketData.Domain.Entities.Enums;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects;

namespace ProfitFox.MarketData.Application.Commands.History.TopUpFunding;

internal sealed class TopUpFundingCommandHandler(IExchangeMarketData exchange, IMarketStore store, TimeProvider time) : IRequestHandler<TopUpFundingCommand>
{
    private readonly HistoryLoaderService _loader = new HistoryLoaderService(exchange, store);

    public async Task Handle(TopUpFundingCommand request, CancellationToken cancellationToken)
    {
        foreach (MarketPair pair in request.Pairs.Where(pair => pair.Instrument.Category == MarketCategory.Linear))
        {
            SeriesKey funding = SeriesKey.Of(DataKind.Funding, pair.Instrument);
            Coverage coverage = await store.GetCoverageAsync(funding, cancellationToken);

            if (coverage.Ranges.Count > 0)
            {
                await _loader.LoadAsync(funding, new TimeRange(coverage.Ranges[^1].To, time.GetUtcNow()), cancellationToken);
            }
        }
    }
}
