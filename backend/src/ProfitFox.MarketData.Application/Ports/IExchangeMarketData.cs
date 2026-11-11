using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Domain.Entities;
using ProfitFox.MarketData.Domain.Entities.Enums;
using ProfitFox.MarketData.Domain.ValueObjects;

namespace ProfitFox.MarketData.Application.Ports;

/// <summary>The public data of the exchange over REST, within the service's own limit of requests.</summary>
/// <remarks>
///     The reads of history throw <see cref="ExchangeRateLimitedException" /> when the exchange says the service sends too many requests,
///     and while the pause that follows lasts, without sending a request; <see cref="ExchangeUnavailableException" /> when the exchange
///     cannot answer for now.
/// </remarks>
public interface IExchangeMarketData
{
    /// <summary>Every instrument the exchange lists in the category.</summary>
    Task<IReadOnlyList<ListedInstrument>> ListInstrumentsAsync(MarketCategory category, CancellationToken cancellationToken);

    /// <summary>Every closed candle of the interval with open time in <c>[from, to)</c>, oldest first; the adapter goes page by page.</summary>
    Task<IReadOnlyList<Candle>> GetCandlesAsync(Instrument instrument, Interval interval, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);

    /// <summary>The 5 minute points of open interest of a linear pair in <c>[from, to)</c>, oldest first.</summary>
    Task<IReadOnlyList<OpenInterestPoint>> GetOpenInterestAsync(Instrument instrument, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);

    /// <summary>The open interest of every linear pair now, in coins of the contract, from one request of the tickers.</summary>
    Task<IReadOnlyList<(Instrument Instrument, decimal OpenInterest)>> GetOpenInterestNowAsync(CancellationToken cancellationToken);

    /// <summary>The funding settlements of a linear pair in <c>[from, to)</c>, oldest first.</summary>
    Task<IReadOnlyList<FundingRatePoint>> GetFundingAsync(Instrument instrument, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}