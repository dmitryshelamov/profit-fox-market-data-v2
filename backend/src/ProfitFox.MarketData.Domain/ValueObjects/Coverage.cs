namespace ProfitFox.MarketData.Domain.ValueObjects;

/// <summary>
///     The periods of a series whose data is whole, as disjoint ranges in time order. Inside them a missing candle or minute of trades had
///     no trades; outside them the data is unknown.
/// </summary>
public sealed class Coverage
{
    public static readonly Coverage Empty = new Coverage([]);

    public IReadOnlyList<TimeRange> Ranges { get; }

    private Coverage(IReadOnlyList<TimeRange> ranges)
    {
        Ranges = ranges;
    }

    /// <summary>The coverage with <paramref name="range" /> in it: every range it touches or overlaps joins it into one.</summary>
    public Coverage Add(TimeRange range)
    {
        DateTimeOffset from = range.From;
        DateTimeOffset to = range.To;
        var apart = new List<TimeRange>();

        foreach (TimeRange covered in Ranges)
        {
            if (covered.To < from || covered.From > to)
            {
                apart.Add(covered);
            }
            else
            {
                from = covered.From < from ? covered.From : from;
                to = covered.To > to ? covered.To : to;
            }
        }

        apart.Add(new TimeRange(from, to));

        return new Coverage([.. apart.OrderBy(covered => covered.From)]);
    }

    /// <summary>The parts of <paramref name="period" /> outside the coverage, in time order.</summary>
    public IReadOnlyList<TimeRange> Missing(TimeRange period)
    {
        var missing = new List<TimeRange>();
        DateTimeOffset cursor = period.From;

        foreach (TimeRange covered in Ranges.Where(covered => covered.To > period.From && covered.From < period.To))
        {
            if (covered.From > cursor)
            {
                missing.Add(new TimeRange(cursor, covered.From));
            }

            // The ranges are disjoint and in time order, so each one ends past the cursor.
            cursor = covered.To;
        }

        if (cursor < period.To)
        {
            missing.Add(new TimeRange(cursor, period.To));
        }

        return missing;
    }
}