using ProfitFox.MarketData.Application.Ports;
using ProfitFox.MarketData.Domain.Entities;
using ProfitFox.MarketData.Domain.Entities.Enums;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects;

namespace ProfitFox.MarketData.Application.Services.History;

/// <summary>Loads a period of a series from the exchange and stores it with its coverage: candles page by page from new to old.</summary>
internal sealed class HistoryLoaderService(IExchangeMarketData exchange, IMarketStore store)
{
    // The most candles of one page of the exchange.
    private const int PageSize = 1000;

    private static readonly TimeSpan Step = TimeSpan.FromMinutes(5);

    /// <summary>Whether the exchange keeps the history of the kind.</summary>
    public static bool Loads(DataKind kind)
    {
        return kind is DataKind.Candles or DataKind.OpenInterest or DataKind.Funding;
    }

    /// <summary>The end of what may be loaded at <paramref name="now" />: the start of the forming candle, of the current step of open interest, or now.</summary>
    public static DateTimeOffset ClosedUntil(SeriesKey series, DateTimeOffset now)
    {
        return series.Kind switch
        {
            DataKind.Candles => series.Interval!.Value.CandleOpen(now),
            DataKind.OpenInterest => new DateTimeOffset(now.UtcTicks - (now.UtcTicks % Step.Ticks), TimeSpan.Zero),
            _ => now
        };
    }

    public async Task LoadAsync(SeriesKey series, TimeRange period, CancellationToken cancellationToken)
    {
        switch (series.Kind)
        {
            case DataKind.Candles:
                await LoadCandlesAsync(series, period, cancellationToken);
                break;
            case DataKind.OpenInterest:
                IReadOnlyList<OpenInterestPoint> points = await exchange.GetOpenInterestAsync(series.Instrument, period.From, period.To, cancellationToken);
                await store.ReplaceAsync(series, period, SeriesRows.OfOpenInterest(points), cancellationToken);
                break;
            default:
                IReadOnlyList<FundingRatePoint> rates = await exchange.GetFundingAsync(series.Instrument, period.From, period.To, cancellationToken);
                await store.ReplaceAsync(series, period, SeriesRows.OfFunding(rates), cancellationToken);
                break;
        }
    }

    private async Task LoadCandlesAsync(SeriesKey series, TimeRange period, CancellationToken cancellationToken)
    {
        Interval interval = series.Interval!.Value;
        TimeSpan page = (interval.CandleEnd(DateTimeOffset.UnixEpoch) - DateTimeOffset.UnixEpoch) * PageSize;

        for (DateTimeOffset upper = period.To; upper > period.From;)
        {
            DateTimeOffset lower = upper - page > period.From ? upper - page : period.From;
            IReadOnlyList<Candle> candles = await exchange.GetCandlesAsync(series.Instrument, interval, lower, upper, cancellationToken);
            await store.ReplaceAsync(series, new TimeRange(lower, upper), SeriesRows.OfCandles(candles), cancellationToken);
            upper = lower;
        }
    }
}
