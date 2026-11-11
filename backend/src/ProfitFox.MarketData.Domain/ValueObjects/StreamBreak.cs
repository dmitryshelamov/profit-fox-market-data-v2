using ProfitFox.MarketData.Domain.Entities.Enums;

namespace ProfitFox.MarketData.Domain.ValueObjects;

/// <summary>A break of a stream of the exchange: the name of the stream, when it was lost and when it was back.</summary>
public sealed record StreamBreak(LiveStream Stream, DateTimeOffset LostAt, DateTimeOffset RestoredAt);