using MediatR;

using ProfitFox.MarketData.Domain.ValueObjects;

namespace ProfitFox.MarketData.Application.Queries.History.GetHistory;

/// <summary>The data of a series in a period; what lies outside the coverage is loaded first where the exchange keeps it.</summary>
public sealed record GetHistoryQuery(SeriesKey Series, TimeRange Period) : IRequest<HistoryResult>;