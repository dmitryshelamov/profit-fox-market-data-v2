using ProfitFox.MarketData.Domain.Entities.Enums;

namespace ProfitFox.MarketData.Domain.ValueObjects;

/// <summary>A series of data: a kind of data of a pair and, for candles only, their interval.</summary>
public sealed record SeriesKey
{
    private static readonly long StepTicks = TimeSpan.FromMinutes(5).Ticks;

    public DataKind Kind { get; }

    public Instrument Instrument { get; }

    public Interval? Interval { get; }

    private SeriesKey(DataKind kind, Instrument instrument, Interval? interval)
    {
        Kind = kind;
        Instrument = instrument;
        Interval = interval;
    }

    public static SeriesKey Candles(Instrument instrument, Interval interval)
    {
        return new SeriesKey(DataKind.Candles, instrument, interval);
    }

    /// <exception cref="ArgumentException"><paramref name="kind" /> is candles, which need an interval.</exception>
    public static SeriesKey Of(DataKind kind, Instrument instrument)
    {
        if (kind == DataKind.Candles)
        {
            throw new ArgumentException("A series of candles needs an interval.", nameof(kind));
        }

        return new SeriesKey(kind, instrument, interval: null);
    }

    /// <summary>The end of the closed data of the series at <paramref name="now" />: the start of the forming candle, of the current step of open interest, or now.</summary>
    public DateTimeOffset ClosedUntil(DateTimeOffset now)
    {
        return Kind switch
        {
            DataKind.Candles => Interval!.Value.CandleOpen(now),
            DataKind.OpenInterest => new DateTimeOffset(now.UtcTicks - now.UtcTicks % StepTicks, TimeSpan.Zero),
            _ => now
        };
    }
}