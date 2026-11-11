using System.Collections.Specialized;
using System.Net;
using System.Web;

namespace ProfitFox.MarketData.Tests.Shared.Bybit;

/// <summary>A tiny Bybit v5 stand-in that replaces every outgoing HTTP call of the app, so no test reaches the real Bybit.</summary>
public class FakeBybitHandler : HttpMessageHandler
{
    private int _requests;

    /// <summary>Instruments Bybit lists, as (category, symbol).</summary>
    public HashSet<(string Category, string Symbol)> Instruments { get; } = [];

    /// <summary>Base and quote coins of listed instruments; an instrument without them is listed with empty coins.</summary>
    public Dictionary<(string Category, string Symbol), (string BaseCoin, string QuoteCoin)> Coins { get; } = [];

    /// <summary>The status Bybit lists an instrument with, such as <c>Closed</c> or <c>PreLaunch</c>; without one, <c>Trading</c>.</summary>
    public Dictionary<(string Category, string Symbol), string> Statuses { get; } = [];

    /// <summary>The contract type of a listed linear instrument, such as <c>LinearFutures</c>; without one, <c>LinearPerpetual</c>.</summary>
    public Dictionary<string, string> ContractTypes { get; } = [];

    /// <summary>The launch time Bybit lists a linear instrument with; without one, 2020-01-01. Spot instruments have none.</summary>
    public Dictionary<string, DateTimeOffset> LaunchTimes { get; } = [];

    /// <summary>Instruments per page of a linear list without a symbol; without it Bybit's own <c>limit</c> decides. Spot is never paged.</summary>
    public int? LinearPageSize { get; set; }

    /// <summary>The <c>category</c> of every instruments-info request without a symbol, in order.</summary>
    public RequestLog<string> ListRequests { get; } = new RequestLog<string>();

    /// <summary>
    ///     The candle history Bybit knows, rows <c>[openTime ISO, open, high, low, close, volume, turnover]</c> in any order. A kline
    ///     request gets the rows with open time in its inclusive <c>[start, end]</c>, newest first, at most <c>limit</c> of them.
    /// </summary>
    public List<string[]> Klines { get; } = [];

    /// <summary>The <c>[start, end]</c> of every kline request, in order.</summary>
    public RequestLog<(DateTimeOffset Start, DateTimeOffset End)> KlineRequests { get; } = new RequestLog<(DateTimeOffset Start, DateTimeOffset End)>();

    /// <summary>Points of open interest of linear symbols at 5 minutes, as (symbol, time, value in coins).</summary>
    public List<(string Symbol, DateTimeOffset Time, string Value)> OpenInterest { get; } = [];

    /// <summary>Points of open interest per page; without it Bybit's own <c>limit</c> decides.</summary>
    public int? OpenInterestPageSize { get; set; }

    /// <summary>The query of every open interest request, in order.</summary>
    public RequestLog<string> OpenInterestRequests { get; } = new RequestLog<string>();

    /// <summary>Funding settlements of linear symbols, as (symbol, time, rate).</summary>
    public List<(string Symbol, DateTimeOffset Time, string Rate)> FundingRates { get; } = [];

    /// <summary>The <c>[startTime, endTime]</c> of every funding history request, in order.</summary>
    public RequestLog<(DateTimeOffset Start, DateTimeOffset End)> FundingRequests { get; } = new RequestLog<(DateTimeOffset Start, DateTimeOffset End)>();

    /// <summary>The open interest in coins the linear tickers of Bybit report now, by symbol.</summary>
    public Dictionary<string, string> LinearTickers { get; } = [];

    /// <summary>The category of every tickers request, in order.</summary>
    public RequestLog<string> TickerRequests { get; } = new RequestLog<string>();

    /// <summary>How many HTTP requests the app made, answered or not.</summary>
    public int Requests => Volatile.Read(ref _requests);

    public bool Unavailable { get; set; }

    /// <summary>
    ///     Every request gets the answer Bybit gives requests over its limit: <c>429</c> (HTTP 429), <c>10006</c> (the code in an OK
    ///     answer) or <c>403</c> (HTTP 403, a ban of the IP); <c>null</c> answers as usual.
    /// </summary>
    public string? RateLimit { get; set; }

    /// <summary>Kline requests get an error Bybit does not ask to retry.</summary>
    public bool RejectKlines { get; set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _requests);

        if (Unavailable)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }

        if (RateLimit is { } limit)
        {
            return Task.FromResult(
                limit switch
                {
                    "429" => new HttpResponseMessage(HttpStatusCode.TooManyRequests),
                    "403" => new HttpResponseMessage(HttpStatusCode.Forbidden),
                    _ => new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("""{"retCode":10006,"retMsg":"Too many visits!","result":{},"retExtInfo":{},"time":0}""")
                    }
                });
        }

        if (RejectKlines && request.RequestUri!.AbsolutePath == "/v5/market/kline")
        {
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"retCode":10001,"retMsg":"params error: symbol invalid","result":{},"retExtInfo":{},"time":0}""")
                });
        }

        NameValueCollection query = HttpUtility.ParseQueryString(request.RequestUri!.Query);
        string result = request.RequestUri.AbsolutePath switch
        {
            "/v5/market/instruments-info" => InstrumentsList(query),
            "/v5/market/kline" => Kline(query),
            "/v5/market/open-interest" => OpenInterestOf(query),
            "/v5/market/funding/history" => FundingOf(query),
            "/v5/market/tickers" => TickersOf(query["category"]!),
            _ => throw new InvalidOperationException($"Unexpected Bybit request {request.RequestUri}")
        };
        var json = $$"""{"retCode":0,"retMsg":"OK","result":{{result}},"retExtInfo":{},"time":0}""";

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
    }

    private string InstrumentsList(NameValueCollection query)
    {
        string category = query["category"]!;
        ListRequests.Record(category);
        List<(string Category, string Symbol)> all = Instruments.Where(instrument => instrument.Category == category)
            .OrderBy(instrument => instrument.Symbol, StringComparer.Ordinal)
            .ToList();
        int pageSize = category == "linear" ? LinearPageSize ?? int.Parse(query["limit"] ?? "500") : all.Count;
        int offset = int.Parse(query["cursor"] ?? "0");
        IEnumerable<string> items = all.Skip(offset)
            .Take(pageSize)
            .Select(instrument =>
            {
                (string baseCoin, string quoteCoin) = Coins.GetValueOrDefault(instrument, ("", ""));

                string status = Statuses.GetValueOrDefault(instrument, "Trading");
                long launch = LaunchTimes.GetValueOrDefault(
                        instrument.Symbol,
                        new DateTimeOffset(year: 2020, month: 1, day: 1, hour: 0, minute: 0, second: 0, TimeSpan.Zero))
                    .ToUnixTimeMilliseconds();
                string contract = category == "linear"
                    ? $",\"contractType\":\"{ContractTypes.GetValueOrDefault(instrument.Symbol, "LinearPerpetual")}\",\"launchTime\":\"{launch}\""
                    : "";

                return $$"""{"symbol":"{{instrument.Symbol}}","baseCoin":"{{baseCoin}}","quoteCoin":"{{quoteCoin}}","status":"{{status}}"{{contract}}}""";
            });
        string cursor = category == "linear" && offset + pageSize < all.Count ? (offset + pageSize).ToString() : "";

        return $$"""{"category":"{{category}}","list":[{{string.Join(",", items)}}],"nextPageCursor":"{{cursor}}"}""";
    }

    private string Kline(NameValueCollection query)
    {
        DateTimeOffset start = DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(query["start"]!));
        DateTimeOffset end = DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(query["end"]!));
        KlineRequests.Record((start, end));
        IEnumerable<string> items = Klines
            .Select(row => (OpenTime: DateTimeOffset.Parse(row[0]), Row: row))
            .Where(candle => candle.OpenTime >= start && candle.OpenTime <= end)
            .OrderByDescending(candle => candle.OpenTime)
            .Take(int.Parse(query["limit"]!))
            .Select(candle => $"[\"{candle.OpenTime.ToUnixTimeMilliseconds()}\",{string.Join(",", candle.Row.Skip(1).Select(value => $"\"{value}\""))}]");

        return $$"""{"list":[{{string.Join(",", items)}}]}""";
    }

    private string TickersOf(string category)
    {
        TickerRequests.Record(category);
        IEnumerable<string> items = LinearTickers.Select(ticker =>
            $$"""{"symbol":"{{ticker.Key}}","bid1Price":"1","bid1Size":"1","ask1Price":"1","ask1Size":"1","lastPrice":"1","prevPrice24h":"1","price24hPcnt":"0","highPrice24h":"1","lowPrice24h":"1","turnover24h":"0","volume24h":"0","markPrice":"1","indexPrice":"1","openInterest":"{{ticker.Value}}","openInterestValue":"0","fundingRate":"0","nextFundingTime":"0","deliveryTime":"0","deliveryFeeRate":"","predictedDeliveryPrice":"","basisRate":"","basis":"","prevPrice1h":"1"}""");

        return $$"""{"category":"{{category}}","list":[{{string.Join(",", items)}}]}""";
    }

    // Newest first in the inclusive [startTime, endTime], a page at a time; the cursor is the offset of the next page.
    private string OpenInterestOf(NameValueCollection query)
    {
        OpenInterestRequests.Record(query.ToString()!);
        string symbol = query["symbol"]!;
        DateTimeOffset start = DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(query["startTime"]!));
        DateTimeOffset end = DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(query["endTime"]!));
        List<(string Symbol, DateTimeOffset Time, string Value)> all = OpenInterest.Where(point => point.Symbol == symbol && point.Time >= start && point.Time <= end)
            .OrderByDescending(point => point.Time)
            .ToList();
        int pageSize = OpenInterestPageSize ?? int.Parse(query["limit"] ?? "50");
        int offset = int.Parse(string.IsNullOrEmpty(query["cursor"]) ? "0" : query["cursor"]!);
        IEnumerable<string> items = all.Skip(offset)
            .Take(pageSize)
            .Select(point => $$"""{"openInterest":"{{point.Value}}","timestamp":"{{point.Time.ToUnixTimeMilliseconds()}}"}""");
        string cursor = offset + pageSize < all.Count ? (offset + pageSize).ToString() : "";

        return $$"""{"category":"linear","symbol":"{{symbol}}","list":[{{string.Join(",", items)}}],"nextPageCursor":"{{cursor}}"}""";
    }

    // Newest first in the inclusive [startTime, endTime], at most `limit`, as Bybit pages funding history: by time, without a cursor.
    private string FundingOf(NameValueCollection query)
    {
        string symbol = query["symbol"]!;
        DateTimeOffset start = DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(query["startTime"]!));
        DateTimeOffset end = DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(query["endTime"]!));
        FundingRequests.Record((start, end));
        IEnumerable<string> items = FundingRates.Where(rate => rate.Symbol == symbol && rate.Time >= start && rate.Time <= end)
            .OrderByDescending(rate => rate.Time)
            .Take(int.Parse(query["limit"] ?? "200"))
            .Select(rate => $$"""{"symbol":"{{symbol}}","fundingRate":"{{rate.Rate}}","fundingRateTimestamp":"{{rate.Time.ToUnixTimeMilliseconds()}}"}""");

        return $$"""{"category":"linear","list":[{{string.Join(",", items)}}]}""";
    }
}