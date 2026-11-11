using System.Globalization;

using ProfitFox.MarketData.Application.Services.History;
using ProfitFox.MarketData.Domain.Entities;
using ProfitFox.MarketData.Domain.Entities.Enums;
using ProfitFox.MarketData.Domain.ValueObjects;

using Shouldly;

namespace ProfitFox.MarketData.Tests.Unit.Application.Services.History;

/// <summary>Requirement «История при запуске»: what of the window is missing, by days from new to old.</summary>
public class HistoryWindowServiceTests
{
    private static readonly DateTimeOffset Now = Time("2026-11-10T12:30:20Z");
    private static readonly Instrument Btc = new Instrument(MarketCategory.Linear, "BTCUSDT");
    private static readonly SeriesKey Minutes = SeriesKey.Candles(Btc, Interval.OneMinute);
    private static readonly MarketPair Pair = new MarketPair(Btc, true, null);

    // Scenario «Первый запуск»: the freshest day first.
    [Test]
    public void Empty_series_misses_the_whole_window_by_days_from_new_to_old()
    {
        HistoryWindowService.Plan(Minutes, Pair, Coverage.Empty, Now, days: 2).ShouldBe(
        [
            Range("2026-11-10T00:00:00Z", "2026-11-10T12:30:00Z"),
            Range("2026-11-09T00:00:00Z", "2026-11-10T00:00:00Z"),
            Range("2026-11-08T12:30:00Z", "2026-11-09T00:00:00Z")
        ]);
    }

    // Scenario «Число дней увеличили»: only what the coverage does not hold.
    [Test]
    public void Covered_part_of_the_window_is_not_planned()
    {
        Coverage covered = Coverage.Empty.Add(Range("2026-11-09T00:00:00Z", "2026-11-10T12:30:00Z"));

        HistoryWindowService.Plan(Minutes, Pair, covered, Now, days: 2).ShouldBe([Range("2026-11-08T12:30:00Z", "2026-11-09T00:00:00Z")]);
    }

    // Scenario «Пара моложе окна».
    [Test]
    public void Window_of_a_young_pair_starts_at_its_launch()
    {
        var young = Pair with { ListedAt = Time("2026-11-10T06:00:00Z") };

        HistoryWindowService.Plan(Minutes, young, Coverage.Empty, Now, days: 2).ShouldBe([Range("2026-11-10T06:00:00Z", "2026-11-10T12:30:00Z")]);
    }

    [Test]
    public void Pair_launched_after_the_last_closed_candle_misses_nothing()
    {
        var future = Pair with { ListedAt = Time("2026-11-10T13:00:00Z") };

        HistoryWindowService.Plan(Minutes, future, Coverage.Empty, Now, days: 2).ShouldBeEmpty();
    }

    private static TimeRange Range(string from, string to)
    {
        return new TimeRange(Time(from), Time(to));
    }

    private static DateTimeOffset Time(string text)
    {
        return DateTimeOffset.Parse(text, CultureInfo.InvariantCulture);
    }
}
