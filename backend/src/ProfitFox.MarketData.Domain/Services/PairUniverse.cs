using ProfitFox.MarketData.Domain.Entities;
using ProfitFox.MarketData.Domain.Entities.Enums;
using ProfitFox.MarketData.Domain.ValueObjects;

namespace ProfitFox.MarketData.Domain.Services;

/// <summary>The pairs the service collects: the trading USDT pairs of spot and the trading USDT perpetuals of linear.</summary>
public static class PairUniverse
{
    private const string Quote = "USDT";

    /// <summary>The instruments of <paramref name="listed" /> the service collects; non-empty <paramref name="symbols" /> narrow them, in any case.</summary>
    public static IReadOnlyList<ListedInstrument> Select(IEnumerable<ListedInstrument> listed, IReadOnlyCollection<string> symbols)
    {
        return
        [
            .. listed
                .Where(instrument => instrument.Trading && instrument.QuoteCoin == Quote && (instrument.Category == MarketCategory.Spot || instrument.Perpetual))
                .Where(instrument => symbols.Count == 0 || symbols.Contains(instrument.Symbol, StringComparer.OrdinalIgnoreCase))
        ];
    }

    /// <summary>
    ///     The pairs that change once <paramref name="selected" /> is the list: a new pair, a pair back in trading and a pair that is no
    ///     longer listed. A pair keeps its data either way.
    /// </summary>
    public static IReadOnlyList<MarketPair> Merge(IReadOnlyList<MarketPair> known, IReadOnlyList<ListedInstrument> selected)
    {
        Dictionary<Instrument, ListedInstrument> wanted = selected.ToDictionary(Instrument);
        HashSet<Instrument> knownInstruments = known.Select(pair => pair.Instrument).ToHashSet();

        return
        [
            .. known
                .Select(pair => pair with { Listed = wanted.ContainsKey(pair.Instrument) })
                .Where(pair => !known.Contains(pair)),
            .. selected
                .Where(listed => !knownInstruments.Contains(Instrument(listed)))
                .Select(listed => new MarketPair(Instrument(listed), Listed: true, listed.ListedAt))
        ];
    }

    private static Instrument Instrument(ListedInstrument listed)
    {
        return new Instrument(listed.Category, listed.Symbol);
    }
}