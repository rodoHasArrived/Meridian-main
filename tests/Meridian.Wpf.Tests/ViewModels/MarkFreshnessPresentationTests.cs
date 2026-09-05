using Meridian.Contracts.Workstation;
using Meridian.Wpf.Models;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class MarkFreshnessPresentationTests
{
    [Theory]
    [InlineData("2026-08-01", 34, "AAPL observation is stale.")]
    [InlineData(null, null, "AAPL observation is missing.")]
    [InlineData("2026-09-05", -1, "AAPL observation follows the valuation date.")]
    public void AccountAndAggregateInspectors_ShowSharedBlockAndRecover(string? observed, int? age, string reason)
    {
        var blocked = Assessment(observed, age, "ReviewRequired", reason);
        var account = new AccountPositionRow("AAPL", 10, "Long", 90m, 100m, 0m, blocked);
        var aggregate = new AggregatedPositionRow("AAPL", 10m, 10m, 0m, 90m, 100m, 1, blocked);

        foreach (var inspector in new[] { AccountPortfolioViewModel.BuildPositionInspector(account), AggregatePortfolioViewModel.BuildPositionInspector(aggregate) })
        {
            inspector.Title.Should().Be("AAPL");
            inspector.Detail.Should().Be(reason);
            inspector.Badge!.Value.Should().Be("Review required");
            inspector.Facts.Should().Contain(fact => fact.Label == "Mark observed on" && fact.Value == (observed ?? "Unknown"));
            inspector.Facts.Should().Contain(fact => fact.Label == "Mark age" && fact.Value == (age == null ? "Unknown" : $"{age} day(s)"));
        }

        var current = Assessment("2026-09-04", 0, "Current", null);
        AccountPortfolioViewModel.BuildPositionInspector(account with { MarkFreshness = current }).Badge!.Value.Should().Be("Current");
        AggregatePortfolioViewModel.BuildPositionInspector(aggregate with { MarkFreshness = current }).Badge!.Value.Should().Be("Current");
    }

    [Fact]
    public void MissingAssessment_CannotPresentAnApprovedValuation()
    {
        var mark = new MarkFreshnessPresentation(null);
        mark.ReviewRequired.Should().BeTrue();
        mark.RecordedValue(100m).Should().Contain("review required");
        mark.ObservedOn.Should().Be("Unknown");
        mark.Reason.Should().Contain("Shared mark assessment unavailable");
    }

    [Fact]
    public void SharedDecision_IsNotRecomputedUsingDesktopAgeThreshold()
    {
        var mark = new MarkFreshnessPresentation(Assessment("2026-07-26", 40, "Current", null));
        mark.ReviewRequired.Should().BeFalse();
        mark.Age.Should().Be("40 day(s)");
    }

    private static MarkFreshnessAssessmentDto Assessment(string? observed, int? age, string status, string? reason)
        => new("AAPL", Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "account-1",
            new DateOnly(2026, 9, 4), observed is null ? null : DateOnly.Parse(observed), age, "marks-v2", status, reason);
}
