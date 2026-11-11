namespace ProfitFox.MarketData.Domain.ValueObjects;

/// <summary>A period <c>[From, To)</c>: the start is in it, the end is not.</summary>
public sealed record TimeRange
{
    public DateTimeOffset From { get; }

    public DateTimeOffset To { get; }

    /// <exception cref="ArgumentException">The end does not follow the start.</exception>
    public TimeRange(DateTimeOffset from, DateTimeOffset to)
    {
        if (to <= from)
        {
            throw new ArgumentException("The end of a period must follow its start.", nameof(to));
        }

        From = from;
        To = to;
    }
}