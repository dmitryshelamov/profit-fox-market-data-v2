using ProfitFox.MarketData.Domain.ValueObjects;

namespace ProfitFox.MarketData.Domain.Repositories;

/// <summary>The stored data of the series and their coverage.</summary>
public interface IMarketStore
{
    Task<Coverage> GetCoverageAsync(SeriesKey series, CancellationToken cancellationToken);

    /// <summary>The stored data of the series in <paramref name="period" />, oldest first.</summary>
    Task<SeriesRows> ReadAsync(SeriesKey series, TimeRange period, CancellationToken cancellationToken);

    /// <summary>
    ///     Replaces the data of the series in <paramref name="period" /> with <paramref name="rows" /> and adds the period to its coverage,
    ///     in one transaction.
    /// </summary>
    Task ReplaceAsync(SeriesKey series, TimeRange period, SeriesRows rows, CancellationToken cancellationToken);
}
