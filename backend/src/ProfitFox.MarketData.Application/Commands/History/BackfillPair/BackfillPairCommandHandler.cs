using MediatR;

using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Application.Ports;
using ProfitFox.MarketData.Application.Services.History;
using ProfitFox.MarketData.Domain.Entities.Enums;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects;

namespace ProfitFox.MarketData.Application.Commands.History.BackfillPair;

internal sealed class BackfillPairCommandHandler(IExchangeMarketData exchange, IMarketStore store, TimeProvider time) : IRequestHandler<BackfillPairCommand, bool>
{
    private readonly HistoryLoaderService _loader = new HistoryLoaderService(exchange, store);

    public async Task<bool> Handle(BackfillPairCommand request, CancellationToken cancellationToken)
    {
        Instrument instrument = request.Pair.Instrument;
        SeriesKey[] series = instrument.Category == MarketCategory.Linear
            ? [SeriesKey.Candles(instrument, Interval.OneMinute), SeriesKey.Of(DataKind.OpenInterest, instrument), SeriesKey.Of(DataKind.Funding, instrument)]
            : [SeriesKey.Candles(instrument, Interval.OneMinute)];

        try
        {
            foreach (SeriesKey key in series)
            {
                Coverage coverage = await store.GetCoverageAsync(key, cancellationToken);

                foreach (TimeRange range in HistoryWindowService.Plan(key, request.Pair, coverage, time.GetUtcNow(), request.Days))
                {
                    await _loader.LoadAsync(key, range, cancellationToken);
                }
            }
        }
        catch (Exception exception) when (exception is ExchangeUnavailableException or ExchangeRateLimitedException)
        {
            return false;
        }

        return true;
    }
}
