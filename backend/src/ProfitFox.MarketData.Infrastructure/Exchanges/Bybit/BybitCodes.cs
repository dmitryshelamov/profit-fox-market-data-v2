using Bybit.Net.Enums;

using ProfitFox.MarketData.Domain.Entities.Enums;

using Interval = ProfitFox.MarketData.Domain.Entities.Enums.Interval;

namespace ProfitFox.MarketData.Infrastructure.Exchanges.Bybit;

/// <summary>Bybit.Net enums for domain values; they are Bybit's contract and may change on their own.</summary>
internal static class BybitCodes
{
    private static readonly Dictionary<MarketCategory, Category> Categories = new Dictionary<MarketCategory, Category>
    {
        [MarketCategory.Spot] = Category.Spot,
        [MarketCategory.Linear] = Category.Linear
    };

    private static readonly Dictionary<Interval, KlineInterval> Intervals = new Dictionary<Interval, KlineInterval>
    {
        [Interval.OneMinute] = KlineInterval.OneMinute,
        [Interval.ThreeMinutes] = KlineInterval.ThreeMinutes,
        [Interval.FiveMinutes] = KlineInterval.FiveMinutes,
        [Interval.FifteenMinutes] = KlineInterval.FifteenMinutes,
        [Interval.ThirtyMinutes] = KlineInterval.ThirtyMinutes,
        [Interval.OneHour] = KlineInterval.OneHour,
        [Interval.TwoHours] = KlineInterval.TwoHours,
        [Interval.FourHours] = KlineInterval.FourHours,
        [Interval.SixHours] = KlineInterval.SixHours,
        [Interval.TwelveHours] = KlineInterval.TwelveHours,
        [Interval.OneDay] = KlineInterval.OneDay,
        [Interval.OneWeek] = KlineInterval.OneWeek,
        [Interval.OneMonth] = KlineInterval.OneMonth
    };

    internal static Category ToBybit(this MarketCategory category)
    {
        return Categories[category];
    }

    internal static KlineInterval ToBybit(this Interval interval)
    {
        return Intervals[interval];
    }
}