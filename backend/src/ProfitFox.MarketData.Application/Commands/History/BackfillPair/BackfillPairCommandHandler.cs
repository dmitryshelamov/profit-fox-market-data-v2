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

        DateTimeOffset now = time.GetUtcNow();

        try
        {
            foreach (SeriesKey key in series)
            {
                DateTimeOffset until = key.ClosedUntil(now);
                DateTimeOffset from = until - TimeSpan.FromDays(request.Days);

                if (request.Pair.ListedAt > from)
                {
                    from = request.Pair.ListedAt.Value;
                }

                if (from >= until)
                {
                    continue;
                }

                Coverage coverage = await store.GetCoverageAsync(key, cancellationToken);

                foreach (TimeRange range in coverage.Missing(new TimeRange(from, until)).Reverse())
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