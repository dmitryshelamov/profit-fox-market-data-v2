using MediatR;

using ProfitFox.MarketData.Domain.Entities;

namespace ProfitFox.MarketData.Application.Commands.History.BackfillPair;

/// <summary>
///     Loads what the history of the pair misses in the window of the last <paramref name="Days" /> days: minute candles, and for linear also
///     open interest and funding. Returns whether the window is whole; <c>false</c> when the exchange could not answer, to try again later.
/// </summary>
public sealed record BackfillPairCommand(MarketPair Pair, int Days) : IRequest<bool>;
