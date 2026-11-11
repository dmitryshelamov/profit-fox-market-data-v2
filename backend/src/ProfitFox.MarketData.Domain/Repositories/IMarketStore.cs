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

    /// <summary>
    ///     Writes the batch of the live lane in one transaction: a candle or a minute of trades replaces the stored one of its minute, a
    ///     liquidation or a break already stored stays once, and the periods of the batch join the coverage of their series.
    /// </summary>
    Task WriteLiveAsync(LiveBatch batch, CancellationToken cancellationToken);

    /// <summary>The end of the latest stored minute candle of any pair, or <c>null</c> without one.</summary>
    Task<DateTimeOffset?> GetLastMinuteEndAsync(CancellationToken cancellationToken);
}