using ProfitFox.MarketData.Application.Ports;
using ProfitFox.MarketData.Domain.Entities.Enums;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects;

namespace ProfitFox.MarketData.Application.Services.History;

/// <summary>Loads a period of a series from the exchange and stores it with its coverage.</summary>
internal sealed class HistoryLoaderService(IExchangeMarketData exchange, IMarketStore store)
{
    /// <summary>Whether the exchange keeps the history of the kind.</summary>
    public static bool Loads(DataKind kind)
    {
        return kind is DataKind.Candles or DataKind.OpenInterest or DataKind.Funding;
    }

    public async Task LoadAsync(SeriesKey series, TimeRange period, CancellationToken cancellationToken)
    {
        Instrument instrument = series.Instrument;
        SeriesRows rows = series.Kind switch
        {
            DataKind.Candles => SeriesRows.OfCandles(await exchange.GetCandlesAsync(instrument, series.Interval!.Value, period.From, period.To, cancellationToken)),
            DataKind.OpenInterest => SeriesRows.OfOpenInterest(await exchange.GetOpenInterestAsync(instrument, period.From, period.To, cancellationToken)),
            _ => SeriesRows.OfFunding(await exchange.GetFundingAsync(instrument, period.From, period.To, cancellationToken))
        };

        await store.ReplaceAsync(series, period, rows, cancellationToken);
    }
}