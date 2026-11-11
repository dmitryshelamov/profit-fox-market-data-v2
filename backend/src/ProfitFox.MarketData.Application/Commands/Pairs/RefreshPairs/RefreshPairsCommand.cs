using MediatR;

using ProfitFox.MarketData.Domain.Entities;

namespace ProfitFox.MarketData.Application.Commands.Pairs.RefreshPairs;

/// <summary>Reads the list of the exchange again; non-empty <paramref name="Symbols" /> narrow it. Returns the pairs to collect.</summary>
public sealed record RefreshPairsCommand(IReadOnlyCollection<string> Symbols) : IRequest<IReadOnlyList<MarketPair>>;