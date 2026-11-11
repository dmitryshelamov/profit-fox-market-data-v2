using ProfitFox.MarketData.Domain.ValueObjects;

namespace ProfitFox.MarketData.Application.Queries.History.GetHistory;

/// <summary>The data of the period, the covered parts of the period and whether the exchange could not fill the rest of it.</summary>
public sealed record HistoryResult(SeriesRows Rows, IReadOnlyList<TimeRange> Coverage, bool ExchangeUnavailable);