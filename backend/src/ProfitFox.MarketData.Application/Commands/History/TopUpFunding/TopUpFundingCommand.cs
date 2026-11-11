using MediatR;

using ProfitFox.MarketData.Domain.Entities;

namespace ProfitFox.MarketData.Application.Commands.History.TopUpFunding;

/// <summary>
///     Decision 5: loads the funding settlements of the linear <paramref name="Pairs" /> since the end of their funding coverage, as the scanner
///     tops them up. A pair without funding coverage is left to its history.
/// </summary>
public sealed record TopUpFundingCommand(IReadOnlyList<MarketPair> Pairs) : IRequest;
