using ProfitFox.MarketData.Domain.Entities;
using ProfitFox.MarketData.Domain.ValueObjects;

namespace ProfitFox.MarketData.Application.Services.Forming;

/// <summary>The last forming minute candle and the current minute of trades of a pair, each none before its first update.</summary>
public sealed record FormingValue(Instrument Instrument, Candle? Candle, TradeFlowMinute? Trades);