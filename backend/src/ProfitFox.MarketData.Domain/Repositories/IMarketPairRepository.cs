using ProfitFox.MarketData.Domain.Entities;
using ProfitFox.MarketData.Domain.ValueObjects;

namespace ProfitFox.MarketData.Domain.Repositories;

/// <summary>The pairs the service has ever collected, listed or not.</summary>
public interface IMarketPairRepository
{
    Task<IReadOnlyList<MarketPair>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>The pair of the instrument, none when the service never collected it.</summary>
    Task<MarketPair?> FindAsync(Instrument instrument, CancellationToken cancellationToken);

    /// <summary>Adds the new pairs and replaces the known ones.</summary>
    Task SaveAsync(IReadOnlyCollection<MarketPair> pairs, CancellationToken cancellationToken);
}
