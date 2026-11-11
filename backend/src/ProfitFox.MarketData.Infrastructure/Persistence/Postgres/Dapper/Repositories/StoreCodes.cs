using ProfitFox.MarketData.Domain.Entities.Enums;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories;

/// <summary>The texts the database stores the enums of the domain by: the codes of Bybit for categories and intervals.</summary>
internal static class StoreCodes
{
    private static readonly Dictionary<MarketCategory, string> Categories = new Dictionary<MarketCategory, string>
    {
        [MarketCategory.Spot] = "spot",
        [MarketCategory.Linear] = "linear"
    };

    private static readonly Dictionary<Interval, string> Intervals = new Dictionary<Interval, string>
    {
        [Interval.OneMinute] = "1",
        [Interval.ThreeMinutes] = "3",
        [Interval.FiveMinutes] = "5",
        [Interval.FifteenMinutes] = "15",
        [Interval.ThirtyMinutes] = "30",
        [Interval.OneHour] = "60",
        [Interval.TwoHours] = "120",
        [Interval.FourHours] = "240",
        [Interval.SixHours] = "360",
        [Interval.TwelveHours] = "720",
        [Interval.OneDay] = "D",
        [Interval.OneWeek] = "W",
        [Interval.OneMonth] = "M"
    };

    private static readonly Dictionary<DataKind, string> Kinds = new Dictionary<DataKind, string>
    {
        [DataKind.Candles] = "candles",
        [DataKind.OpenInterest] = "open_interest",
        [DataKind.Funding] = "funding",
        [DataKind.Liquidations] = "liquidations",
        [DataKind.TradeFlow] = "trade_flow"
    };

    private static readonly Dictionary<string, MarketCategory> CategoriesByCode = Categories.ToDictionary(pair => pair.Value, pair => pair.Key);

    public static string ToCode(this MarketCategory category)
    {
        return Categories[category];
    }

    public static MarketCategory ToCategory(string code)
    {
        return CategoriesByCode[code];
    }

    public static string ToCode(this Interval interval)
    {
        return Intervals[interval];
    }

    public static string ToCode(this DataKind kind)
    {
        return Kinds[kind];
    }

    public static string ToCode(this LiquidationSide side)
    {
        return side == LiquidationSide.Long ? "long" : "short";
    }

    public static LiquidationSide ToLiquidationSide(string code)
    {
        return code == "long" ? LiquidationSide.Long : LiquidationSide.Short;
    }
}