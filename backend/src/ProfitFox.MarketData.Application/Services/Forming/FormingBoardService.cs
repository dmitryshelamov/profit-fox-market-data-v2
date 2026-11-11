using ProfitFox.MarketData.Domain.Entities;
using ProfitFox.MarketData.Domain.ValueObjects;

namespace ProfitFox.MarketData.Application.Services.Forming;

/// <summary>
///     The forming values of every pair in memory only: each update replaces the last one, so a reader gets the latest value whenever it
///     reads, however many updates it missed. A version counts every update; a reader passes the version it read up to. Thread-safe.
/// </summary>
public sealed class FormingBoardService
{
    private readonly Lock _lock = new Lock();
    private readonly Dictionary<Instrument, (FormingValue Value, long Version)> _values = [];
    private long _version;

    public void Update(Instrument instrument, Candle forming)
    {
        lock (_lock)
        {
            _values[instrument] = (Current(instrument) with { Candle = forming }, ++_version);
        }
    }

    public void Update(Instrument instrument, TradeFlowMinute current)
    {
        lock (_lock)
        {
            _values[instrument] = (Current(instrument) with { Trades = current }, ++_version);
        }
    }

    /// <summary>The values of <paramref name="pairs" /> updated after <paramref name="since" />, and the version to read from next time.</summary>
    public (IReadOnlyList<FormingValue> Values, long Version) Changed(IReadOnlyCollection<Instrument> pairs, long since)
    {
        lock (_lock)
        {
            FormingValue[] changed =
                [.. pairs.Where(_values.ContainsKey).Select(pair => _values[pair]).Where(entry => entry.Version > since).Select(entry => entry.Value)];

            return (changed, _version);
        }
    }

    private FormingValue Current(Instrument instrument)
    {
        return _values.TryGetValue(instrument, out (FormingValue Value, long Version) entry) ? entry.Value : new FormingValue(instrument, null, null);
    }
}
