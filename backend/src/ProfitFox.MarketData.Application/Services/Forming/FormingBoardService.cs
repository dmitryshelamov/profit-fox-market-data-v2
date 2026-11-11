using ProfitFox.MarketData.Domain.Entities;
using ProfitFox.MarketData.Domain.ValueObjects;

namespace ProfitFox.MarketData.Application.Services.Forming;

/// <summary>
///     The forming values of every pair in memory only: each update replaces the last one, so a reader gets the latest value whenever it
///     reads, however many updates it missed. Thread-safe.
/// </summary>
public sealed class FormingBoardService
{
    private readonly Lock _lock = new Lock();
    private readonly Dictionary<Instrument, FormingValue> _values = [];

    public void Update(Instrument instrument, Candle forming)
    {
        lock (_lock)
        {
            _values[instrument] = Current(instrument) with { Candle = forming };
        }
    }

    public void Update(Instrument instrument, TradeFlowMinute current)
    {
        lock (_lock)
        {
            _values[instrument] = Current(instrument) with { Trades = current };
        }
    }

    /// <summary>The current values of the pairs of <paramref name="pairs" /> that have any.</summary>
    public IReadOnlyList<FormingValue> Get(IReadOnlyCollection<Instrument> pairs)
    {
        lock (_lock)
        {
            return [.. pairs.Where(_values.ContainsKey).Select(pair => _values[pair])];
        }
    }

    private FormingValue Current(Instrument instrument)
    {
        return _values.GetValueOrDefault(instrument) ?? new FormingValue(instrument, Candle: null, Trades: null);
    }
}