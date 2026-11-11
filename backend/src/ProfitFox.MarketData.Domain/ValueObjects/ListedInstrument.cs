using ProfitFox.MarketData.Domain.Entities.Enums;

namespace ProfitFox.MarketData.Domain.ValueObjects;

/// <summary>
///     An instrument the exchange lists in a market category: its symbol, the coin it is quoted in, whether it trades now, whether it is a
///     perpetual contract (never on spot) and since when it trades, if the exchange tells (on linear only).
/// </summary>
public sealed record ListedInstrument(
    MarketCategory Category,
    string Symbol,
    string QuoteCoin,
    bool Trading = false,
    bool Perpetual = false,
    DateTimeOffset? ListedAt = null);