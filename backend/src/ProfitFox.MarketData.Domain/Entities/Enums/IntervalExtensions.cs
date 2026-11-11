namespace ProfitFox.MarketData.Domain.Entities.Enums;

public static class IntervalExtensions
{
    /// <summary>End of the candle that opens at <paramref name="openTime" />; a monthly candle ends at the next calendar month.</summary>
    public static DateTimeOffset CandleEnd(this Interval interval, DateTimeOffset openTime)
    {
        return interval switch
        {
            Interval.OneMinute => openTime.AddMinutes(1),
            Interval.ThreeMinutes => openTime.AddMinutes(3),
            Interval.FiveMinutes => openTime.AddMinutes(5),
            Interval.FifteenMinutes => openTime.AddMinutes(15),
            Interval.ThirtyMinutes => openTime.AddMinutes(30),
            Interval.OneHour => openTime.AddHours(1),
            Interval.TwoHours => openTime.AddHours(2),
            Interval.FourHours => openTime.AddHours(4),
            Interval.SixHours => openTime.AddHours(6),
            Interval.TwelveHours => openTime.AddHours(12),
            Interval.OneDay => openTime.AddDays(1),
            Interval.OneWeek => openTime.AddDays(7),
            _ => openTime.AddMonths(1)
        };
    }

    /// <summary>
    ///     Open time of the candle that contains <paramref name="at" />: a grid from the UTC epoch for minutes, hours and days,
    ///     Monday 00:00 for a week, the first of the month for a month. A moment on a boundary is its own candle's open time.
    /// </summary>
    public static DateTimeOffset CandleOpen(this Interval interval, DateTimeOffset at)
    {
        DateTimeOffset utc = at.ToUniversalTime();

        switch (interval)
        {
            case Interval.OneWeek:
                int sinceMonday = ((int)utc.DayOfWeek + 6) % 7;

                return new DateTimeOffset(utc.Date.AddDays(-sinceMonday), TimeSpan.Zero);
            case Interval.OneMonth:
                return new DateTimeOffset(utc.Year, utc.Month, day: 1, hour: 0, minute: 0, second: 0, TimeSpan.Zero);
            default:
                TimeSpan length = interval.CandleEnd(DateTimeOffset.UnixEpoch) - DateTimeOffset.UnixEpoch;

                return DateTimeOffset.UnixEpoch + TimeSpan.FromTicks((utc - DateTimeOffset.UnixEpoch).Ticks / length.Ticks * length.Ticks);
        }
    }
}