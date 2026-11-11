using MediatR;

using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Application.Ports;
using ProfitFox.MarketData.Application.Services.History;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects;

namespace ProfitFox.MarketData.Application.Queries.History.GetHistory;

internal sealed class GetHistoryQueryHandler(IExchangeMarketData exchange, IMarketStore store, IMarketPairRepository pairs, TimeProvider time)
    : IRequestHandler<GetHistoryQuery, HistoryResult>
{
    private readonly HistoryLoaderService _loader = new HistoryLoaderService(exchange, store);

    /// <exception cref="NotFoundException">The service never collected the pair.</exception>
    public async Task<HistoryResult> Handle(GetHistoryQuery request, CancellationToken cancellationToken)
    {
        SeriesKey series = request.Series;

        if (await pairs.FindAsync(series.Instrument, cancellationToken) is null)
        {
            throw new NotFoundException($"The pair {series.Instrument.Category} {series.Instrument.Symbol} is not found.");
        }

        Coverage coverage = await store.GetCoverageAsync(series, cancellationToken);
        var unavailable = false;

        if (HistoryLoaderService.Loads(series.Kind))
        {
            DateTimeOffset closedUntil = series.ClosedUntil(time.GetUtcNow());
            TimeRange[] missing = [.. coverage.Missing(request.Period).Where(range => range.From < closedUntil)];

            foreach (TimeRange range in missing)
            {
                try
                {
                    await _loader.LoadAsync(series, new TimeRange(range.From, range.To < closedUntil ? range.To : closedUntil), cancellationToken);
                }
                catch (Exception exception) when (exception is ExchangeUnavailableException or ExchangeRateLimitedException)
                {
                    unavailable = true;

                    break;
                }
            }

            coverage = await store.GetCoverageAsync(series, cancellationToken);
        }

        SeriesRows rows = await store.ReadAsync(series, request.Period, cancellationToken);

        return new HistoryResult(rows, coverage.Within(request.Period), unavailable);
    }
}