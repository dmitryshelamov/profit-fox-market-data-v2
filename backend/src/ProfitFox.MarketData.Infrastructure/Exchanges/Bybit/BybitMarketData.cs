using System.Net;

using Bybit.Net.Enums;
using Bybit.Net.Interfaces.Clients;
using Bybit.Net.Objects.Models.V5;

using CryptoExchange.Net.Objects;
using CryptoExchange.Net.Objects.Errors;

using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Application.Ports;
using ProfitFox.MarketData.Domain.Entities;
using ProfitFox.MarketData.Domain.Entities.Enums;
using ProfitFox.MarketData.Domain.ValueObjects;
using ProfitFox.MarketData.Infrastructure.Exchanges.Retry;

using Interval = ProfitFox.MarketData.Domain.Entities.Enums.Interval;

namespace ProfitFox.MarketData.Infrastructure.Exchanges.Bybit;

/// <summary>The public REST data of Bybit v5; every request goes through the gate of the service.</summary>
internal sealed class BybitMarketData(IBybitRestClient bybit, RestGate gate) : IExchangeMarketData
{
    // The most candles and instruments of one page of Bybit, and the most points of open interest and funding.
    private const int PageSize = 1000;
    private const int HistoryPageSize = 200;

    public async Task<IReadOnlyList<ListedInstrument>> ListInstrumentsAsync(MarketCategory category, CancellationToken cancellationToken)
    {
        if (category == MarketCategory.Spot)
        {
            // Bybit lists all spot instruments in one answer.
            BybitResponse<BybitSpotSymbol> spot = await gate.SendAsync(
                async token => Unwrap(await bybit.V5Api.ExchangeData.GetSpotSymbolsAsync(ct: token)),
                cancellationToken);

            return [.. spot.List.Select(symbol => new ListedInstrument(category, symbol.Name, symbol.QuoteAsset, symbol.Status == SymbolStatus.Trading))];
        }

        var listed = new List<ListedInstrument>();
        string? cursor = null;

        do
        {
            string? pageCursor = cursor;
            BybitResponse<BybitLinearInverseSymbol> page = await gate.SendAsync(
                async token => Unwrap(await bybit.V5Api.ExchangeData.GetLinearInverseSymbolsAsync(Category.Linear, limit: PageSize, cursor: pageCursor, ct: token)),
                cancellationToken);
            listed.AddRange(
                page.List.Select(linear => new ListedInstrument(
                    category,
                    linear.Name,
                    linear.QuoteAsset,
                    linear.Status == SymbolStatus.Trading,
                    linear.ContractType == ContractTypeV5.LinearPerpetual,
                    Utc(linear.LaunchTime))));
            cursor = page.NextPageCursor;
        }
        while (!string.IsNullOrEmpty(cursor));

        return listed;
    }

    /// <summary>Page by page from new to old: a page holds at most 1000 candles of the inclusive end Bybit takes.</summary>
    public async Task<IReadOnlyList<Candle>> GetCandlesAsync(
        Instrument instrument,
        Interval interval,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        TimeSpan page = (interval.CandleEnd(DateTimeOffset.UnixEpoch) - DateTimeOffset.UnixEpoch) * PageSize;
        var candles = new List<Candle>();

        for (DateTimeOffset upper = to; upper > from;)
        {
            DateTimeOffset lower = upper - page > from ? upper - page : from;
            DateTimeOffset end = upper.AddMilliseconds(-1);
            BybitResponse<BybitKline> klines = await gate.SendAsync(
                async token => Unwrap(
                    await bybit.V5Api.ExchangeData.GetKlinesAsync(
                        instrument.Category.ToBybit(),
                        instrument.Symbol,
                        interval.ToBybit(),
                        lower.UtcDateTime,
                        end.UtcDateTime,
                        PageSize,
                        token)),
                cancellationToken);
            candles.AddRange(klines.List.Select(ToCandle));
            upper = lower;
        }

        return [.. candles.OrderBy(candle => candle.OpenTime)];
    }

    /// <summary>Every page of the period, by the cursor of Bybit.</summary>
    public async Task<IReadOnlyList<OpenInterestPoint>> GetOpenInterestAsync(
        Instrument instrument,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var points = new List<OpenInterestPoint>();
        string? cursor = null;

        do
        {
            string? pageCursor = cursor;
            BybitResponse<BybitOpenInterest> page = await gate.SendAsync(
                async token => Unwrap(
                    await bybit.V5Api.ExchangeData.GetOpenInterestAsync(
                        Category.Linear,
                        instrument.Symbol,
                        OpenInterestInterval.FiveMinutes,
                        from.UtcDateTime,
                        to.AddMilliseconds(-1).UtcDateTime,
                        HistoryPageSize,
                        pageCursor,
                        token)),
                cancellationToken);
            points.AddRange(page.List.Select(point => new OpenInterestPoint(Utc(point.Timestamp), point.OpenInterest)));
            cursor = page.NextPageCursor;
        }
        while (!string.IsNullOrEmpty(cursor));

        return [.. points.OrderBy(point => point.Time)];
    }

    /// <summary>Bybit pages funding history by time only: each page ends just before the oldest settlement of the one before.</summary>
    public async Task<IReadOnlyList<FundingRatePoint>> GetFundingAsync(Instrument instrument, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var rates = new List<FundingRatePoint>();
        DateTimeOffset end = to.AddMilliseconds(-1);

        while (true)
        {
            DateTimeOffset pageEnd = end;
            BybitFundingHistory[] page = (await gate.SendAsync(
                async token => Unwrap(
                    await bybit.V5Api.ExchangeData.GetFundingRateHistoryAsync(
                        Category.Linear,
                        instrument.Symbol,
                        from.UtcDateTime,
                        pageEnd.UtcDateTime,
                        HistoryPageSize,
                        token)),
                cancellationToken)).List;
            rates.AddRange(page.Select(rate => new FundingRatePoint(Utc(rate.Timestamp), rate.FundingRate)));

            if (page.Length < HistoryPageSize)
            {
                return [.. rates.OrderBy(rate => rate.Time)];
            }

            end = rates.Min(rate => rate.Time).AddMilliseconds(-1);
        }
    }

    /// <summary>The data of a successful call; a failure worth retrying is a <see cref="TransientExchangeException" />, any other an <see cref="ExchangeException" />.</summary>
    private static T Unwrap<T>(HttpResult<T> result)
    {
        if (result.Success)
        {
            return result.Data!;
        }

        // Rate limit (HTTP 429 or retCode 10006), timeout and no connection come as error types; a 5xx and a ban of the IP for the
        // rate limit (HTTP 403) only by their status.
        if (result.ResponseStatusCode is HttpStatusCode.Forbidden || result.Error!.ErrorType is ErrorType.RateLimitRequest)
        {
            throw new TransientExchangeException(Describe(result), rateLimited: true, result.ResponseStatusCode is HttpStatusCode.Forbidden);
        }

        if (result.ResponseStatusCode is >= HttpStatusCode.InternalServerError || result.Error.ErrorType is ErrorType.Timeout or ErrorType.NetworkError)
        {
            throw new TransientExchangeException(Describe(result));
        }

        throw new ExchangeException(result.Error.Message!);
    }

    // The SDK leaves the message of HTTP 429 empty and writes the others as "<code>: <message>" with an empty code.
    private static string Describe<T>(HttpResult<T> result)
    {
        return result.ResponseStatusCode is { } status && status != HttpStatusCode.OK
            ? $"HTTP {(int)status}"
            : result.Error!.Message!.TrimStart(':', ' ');
    }

    private static Candle ToCandle(BybitKline kline)
    {
        return new Candle(Utc(kline.StartTime), kline.OpenPrice, kline.HighPrice, kline.LowPrice, kline.ClosePrice, kline.Volume, kline.QuoteVolume);
    }

    private static DateTimeOffset Utc(DateTime time)
    {
        return new DateTimeOffset(DateTime.SpecifyKind(time, DateTimeKind.Utc));
    }
}