using MediatR;

using ProfitFox.MarketData.Application.Ports;
using ProfitFox.MarketData.Domain.Entities;
using ProfitFox.MarketData.Domain.Entities.Enums;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.Services;
using ProfitFox.MarketData.Domain.ValueObjects;

namespace ProfitFox.MarketData.Application.Commands.Pairs.RefreshPairs;

internal sealed class RefreshPairsCommandHandler(IExchangeMarketData exchange, IMarketPairRepository pairs)
    : IRequestHandler<RefreshPairsCommand, IReadOnlyList<MarketPair>>
{
    public async Task<IReadOnlyList<MarketPair>> Handle(RefreshPairsCommand request, CancellationToken cancellationToken)
    {
        IReadOnlyList<ListedInstrument> spot = await exchange.ListInstrumentsAsync(MarketCategory.Spot, cancellationToken);
        IReadOnlyList<ListedInstrument> linear = await exchange.ListInstrumentsAsync(MarketCategory.Linear, cancellationToken);
        IReadOnlyList<MarketPair> known = await pairs.GetAllAsync(cancellationToken);
        IReadOnlyList<MarketPair> changed = PairUniverse.Merge(known, PairUniverse.Select([.. spot, .. linear], request.Symbols));
        await pairs.SaveAsync(changed, cancellationToken);
        var byInstrument = known.ToDictionary(pair => pair.Instrument);

        foreach (MarketPair pair in changed)
        {
            byInstrument[pair.Instrument] = pair;
        }

        return [.. byInstrument.Values.Where(pair => pair.Listed)];
    }
}
