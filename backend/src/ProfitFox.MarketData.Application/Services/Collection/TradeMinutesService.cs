using ProfitFox.MarketData.Domain.ValueObjects;

namespace ProfitFox.MarketData.Application.Services.Collection;

/// <summary>
///     The minutes of the trades stream in memory until they are written: the trades of a pair add up in the minute of their time, and
///     <see cref="CloseMargin" /> after its end the minute goes out. A trade of a minute that went out is dropped. Live minutes are not
///     covered: a minute the subscription or a break cut is partial, and the rebuild of the day from the archive replaces and covers them.
///     Thread-safe: the stream adds while the lane of writes takes.
/// </summary>
public sealed class TradeMinutesService(TimeProvider time)
{
    /// <summary>How long after the end of a minute it goes out, so the trades of its last moment arrive first.</summary>
    public static readonly TimeSpan CloseMargin = TimeSpan.FromSeconds(5);

    private static readonly long MinuteTicks = TimeSpan.FromMinutes(1).Ticks;

    private readonly Lock _lock = new Lock();
    private readonly Dictionary<(Instrument Instrument, DateTimeOffset OpenTime), TradeFlowMinute> _minutes = [];
    private DateTimeOffset _closedUntil = DateTimeOffset.MinValue;

    public void Add(Instrument instrument, TradeEvent trade)
    {
        TradeFlowMinute started = TradeFlowMinute.Start(trade);

        lock (_lock)
        {
            if (started.OpenTime < _closedUntil)
            {
                return;
            }

            (Instrument, DateTimeOffset) key = (instrument, started.OpenTime);
            _minutes[key] = _minutes.TryGetValue(key, out TradeFlowMinute? minute) ? minute.Add(trade) : started;
        }
    }

    /// <summary>The minutes that ended <see cref="CloseMargin" /> ago or more.</summary>
    public IReadOnlyList<(Instrument Instrument, TradeFlowMinute Minute)> TakeClosed()
    {
        DateTimeOffset due = time.GetUtcNow() - CloseMargin;

        lock (_lock)
        {
            _closedUntil = new DateTimeOffset(due.UtcTicks - due.UtcTicks % MinuteTicks, TimeSpan.Zero);
            (Instrument Instrument, TradeFlowMinute Minute)[] closed =
                [.. _minutes.Where(entry => entry.Key.OpenTime < _closedUntil).Select(entry => (entry.Key.Instrument, entry.Value))];

            foreach ((Instrument instrument, TradeFlowMinute minute) in closed)
            {
                _minutes.Remove((instrument, minute.OpenTime));
            }

            return closed;
        }
    }
}