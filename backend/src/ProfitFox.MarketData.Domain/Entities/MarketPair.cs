using ProfitFox.MarketData.Domain.ValueObjects;

namespace ProfitFox.MarketData.Domain.Entities;

/// <summary>
///     A pair the service collects. A pair that is not <see cref="Listed" /> is out of trading: its data stays, nothing new is written.
///     <see cref="ListedAt" /> is the launch the exchange told when the pair was first seen (linear only); its history does not go before it.
/// </summary>
public sealed record MarketPair(Instrument Instrument, bool Listed, DateTimeOffset? ListedAt);