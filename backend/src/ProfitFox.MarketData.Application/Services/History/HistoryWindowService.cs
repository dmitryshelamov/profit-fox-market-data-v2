using ProfitFox.MarketData.Domain.Entities;
using ProfitFox.MarketData.Domain.ValueObjects;

namespace ProfitFox.MarketData.Application.Services.History;

/// <summary>
///     What the history of a series misses in the window of the last days before its last closed data, by days UTC from new to old: the
///     window of a pair launched later starts at its launch.
/// </summary>
public static class HistoryWindowService
{
    private static readonly TimeSpan Day = TimeSpan.FromDays(1);

    public static IReadOnlyList<TimeRange> Plan(SeriesKey series, MarketPair pair, Coverage coverage, DateTimeOffset now, int days)
    {
        DateTimeOffset until = HistoryLoaderService.ClosedUntil(series, now);
        DateTimeOffset from = until - (Day * days);

        if (pair.ListedAt > from)
        {
            from = pair.ListedAt.Value;
        }

        if (from >= until)
        {
            return [];
        }

        return [.. coverage.Missing(new TimeRange(from, until)).SelectMany(ByDays).OrderByDescending(range => range.From)];
    }

    private static IEnumerable<TimeRange> ByDays(TimeRange range)
    {
        for (DateTimeOffset from = range.From; from < range.To;)
        {
            DateTimeOffset nextDay = new DateTimeOffset(from.UtcDateTime.Date, TimeSpan.Zero) + Day;
            DateTimeOffset to = nextDay < range.To ? nextDay : range.To;

            yield return new TimeRange(from, to);

            from = to;
        }
    }
}
