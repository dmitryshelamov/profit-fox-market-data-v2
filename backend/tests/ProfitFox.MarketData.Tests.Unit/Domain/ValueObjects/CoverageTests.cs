using System.Globalization;

using ProfitFox.MarketData.Domain.ValueObjects;

using Shouldly;

namespace ProfitFox.MarketData.Tests.Unit.Domain.ValueObjects;

/// <summary>Requirement «Покрытие»: the periods of a series whose data is whole, as disjoint ranges.</summary>
public class CoverageTests
{
    private static readonly DateTimeOffset Day = DateTimeOffset.Parse("2026-11-09T00:00:00Z", CultureInfo.InvariantCulture);

    [Test]
    public void Empty_coverage_misses_the_whole_period()
    {
        Coverage.Empty.Missing(Hours(from: 8, to: 20)).ShouldBe([Hours(from: 8, to: 20)]);
    }

    [Test]
    public void Added_range_is_covered()
    {
        Coverage.Empty.Add(Hours(from: 8, to: 20)).Ranges.ShouldBe([Hours(from: 8, to: 20)]);
    }

    [Test]
    public void Touching_range_joins_the_covered_one()
    {
        Coverage coverage = Coverage.Empty.Add(Hours(from: 8, to: 20));

        coverage.Add(Hours(from: 20, to: 24)).Ranges.ShouldBe([Hours(from: 8, to: 24)]);
        coverage.Add(Hours(from: 4, to: 8)).Ranges.ShouldBe([Hours(from: 4, to: 20)]);
    }

    [Test]
    public void Overlapping_range_joins_the_covered_one()
    {
        Coverage.Empty.Add(Hours(from: 8, to: 20)).Add(Hours(from: 10, to: 30)).Ranges.ShouldBe([Hours(from: 8, to: 30)]);
    }

    [Test]
    public void Range_inside_the_coverage_changes_nothing()
    {
        Coverage.Empty.Add(Hours(from: 8, to: 20)).Add(Hours(from: 10, to: 12)).Ranges.ShouldBe([Hours(from: 8, to: 20)]);
    }

    [Test]
    public void Separate_ranges_stay_apart_in_time_order()
    {
        Coverage.Empty.Add(Hours(from: 14, to: 16)).Add(Hours(from: 2, to: 4)).Ranges.ShouldBe([Hours(from: 2, to: 4), Hours(from: 14, to: 16)]);
    }

    [Test]
    public void Range_over_a_gap_joins_both_sides()
    {
        Coverage.Empty.Add(Hours(from: 2, to: 4)).Add(Hours(from: 14, to: 16)).Add(Hours(from: 3, to: 15)).Ranges.ShouldBe([Hours(from: 2, to: 16)]);
    }

    [Test]
    public void Period_inside_the_coverage_misses_nothing()
    {
        Coverage.Empty.Add(Hours(from: 8, to: 20)).Missing(Hours(from: 12, to: 14)).ShouldBeEmpty();
    }

    [Test]
    public void Period_partly_outside_the_coverage_misses_the_outside()
    {
        Coverage coverage = Coverage.Empty.Add(Hours(from: 8, to: 20));

        coverage.Missing(Hours(from: 18, to: 23)).ShouldBe([Hours(from: 20, to: 23)]);
        coverage.Missing(Hours(from: 5, to: 10)).ShouldBe([Hours(from: 5, to: 8)]);
    }

    [Test]
    public void Period_over_two_ranges_misses_before_between_and_after()
    {
        Coverage.Empty.Add(Hours(from: 2, to: 4))
            .Add(Hours(from: 14, to: 16))
            .Missing(Hours(from: 0, to: 20))
            .ShouldBe([Hours(from: 0, to: 2), Hours(from: 4, to: 14), Hours(from: 16, to: 20)]);
    }

    [Test]
    public void Period_after_the_coverage_misses_whole()
    {
        Coverage.Empty.Add(Hours(from: 8, to: 20)).Missing(Hours(from: 21, to: 23)).ShouldBe([Hours(from: 21, to: 23)]);
    }

    [Test]
    public void Period_before_the_coverage_misses_whole()
    {
        Coverage.Empty.Add(Hours(from: 8, to: 20)).Missing(Hours(from: 2, to: 5)).ShouldBe([Hours(from: 2, to: 5)]);
    }

    [Test]
    public void Coverage_within_a_period_is_cut_to_it()
    {
        Coverage.Empty.Add(Hours(from: 0, to: 1))
            .Add(Hours(from: 2, to: 4))
            .Add(Hours(from: 8, to: 20))
            .Add(Hours(from: 22, to: 23))
            .Within(Hours(from: 3, to: 10))
            .ShouldBe([Hours(from: 3, to: 4), Hours(from: 8, to: 10)]);
    }

    // Scenario «Разрыв ещё не закрыт»: live minutes before and after a break leave the break uncovered until it is loaded.
    [Test]
    public void Break_between_live_minutes_stays_uncovered()
    {
        Coverage coverage = Coverage.Empty
            .Add(Minutes(14 * 60, 14 * 60 + 2))
            .Add(Minutes(14 * 60 + 5, 14 * 60 + 6));

        coverage.Missing(Minutes(14 * 60, 14 * 60 + 6)).ShouldBe([Minutes(14 * 60 + 2, 14 * 60 + 5)]);
    }

    private static TimeRange Hours(int from, int to)
    {
        return new TimeRange(Day.AddHours(from), Day.AddHours(to));
    }

    private static TimeRange Minutes(int from, int to)
    {
        return new TimeRange(Day.AddMinutes(from), Day.AddMinutes(to));
    }
}