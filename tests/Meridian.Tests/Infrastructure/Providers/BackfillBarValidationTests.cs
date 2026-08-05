using FluentAssertions;
using Meridian.Contracts.Domain.Models;
using Meridian.Infrastructure.Adapters.Core;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed class BackfillBarValidationTests
{
    private static readonly DateOnly Today = new(2026, 7, 21);

    private static HistoricalBar Bar(DateOnly sessionDate, string symbol = "AAPL")
        => new(symbol, sessionDate, 100m, 101m, 99m, 100.5m, 1_000, Source: "test");

    [Fact]
    public void EvaluateDailyRecency_EmptyResult_IsNotStale()
    {
        BackfillBarValidation.EvaluateDailyRecency([], requestedTo: Today, today: Today)
            .Should().BeNull();
    }

    [Fact]
    public void EvaluateDailyRecency_RecentBars_AreFresh()
    {
        var bars = new[] { Bar(Today.AddDays(-5)), Bar(Today.AddDays(-4)) };

        BackfillBarValidation.EvaluateDailyRecency(bars, requestedTo: null, today: Today)
            .Should().BeNull("a latest bar within the tolerance window satisfies an open-ended request");
    }

    [Fact]
    public void EvaluateDailyRecency_FrozenDataset_IsStaleForOpenEndedRequest()
    {
        // Nasdaq WIKI froze in March 2018; an open-ended request implies "through today".
        var frozen = new[] { Bar(new DateOnly(2018, 3, 27)) };

        var verdict = BackfillBarValidation.EvaluateDailyRecency(frozen, requestedTo: null, today: Today);

        verdict.Should().NotBeNull();
        verdict!.LatestSessionDate.Should().Be(new DateOnly(2018, 3, 27));
        verdict.ExpectedThrough.Should().Be(Today);
        verdict.StaleDays.Should().BeGreaterThan(3000);
        verdict.Description.Should().Contain("2018-03-27");
    }

    [Fact]
    public void EvaluateDailyRecency_HistoricalEraRequest_IsNeverStale()
    {
        // Backfilling an old range on purpose must not trip the recency check.
        var bars = new[] { Bar(new DateOnly(2015, 12, 30)) };

        BackfillBarValidation.EvaluateDailyRecency(bars, requestedTo: new DateOnly(2015, 12, 31), today: Today)
            .Should().BeNull();
    }

    [Fact]
    public void EvaluateDailyRecency_FutureRequestedTo_IsCappedAtToday()
    {
        var bars = new[] { Bar(Today.AddDays(-3)) };

        BackfillBarValidation.EvaluateDailyRecency(bars, requestedTo: Today.AddDays(365), today: Today)
            .Should().BeNull("a requested end date in the future must not make fresh data look stale");
    }

    [Fact]
    public void EvaluateDailyRecency_RespectsCustomTolerance()
    {
        var bars = new[] { Bar(Today.AddDays(-8)) };

        BackfillBarValidation.EvaluateDailyRecency(bars, requestedTo: null, today: Today, staleToleranceDays: 5)
            .Should().NotBeNull();
        BackfillBarValidation.EvaluateDailyRecency(bars, requestedTo: null, today: Today, staleToleranceDays: 10)
            .Should().BeNull();
    }

    [Fact]
    public void RemoveFutureDatedBars_KeepsCleanResultsUntouched()
    {
        var bars = new[] { Bar(Today.AddDays(-2)), Bar(Today.AddDays(-1)), Bar(Today) };

        var filtered = BackfillBarValidation.RemoveFutureDatedBars(bars, out var removed, today: Today);

        removed.Should().Be(0);
        filtered.Should().BeSameAs(bars, "a clean result avoids reallocating the list");
    }

    [Fact]
    public void RemoveFutureDatedBars_DropsBarsBeyondTomorrow()
    {
        var bars = new[]
        {
            Bar(Today.AddDays(-1)),
            Bar(Today.AddDays(1)),  // tolerated: exchange time zones ahead of UTC
            Bar(Today.AddDays(2)),  // provider garbage
            Bar(Today.AddDays(30))  // provider garbage
        };

        var filtered = BackfillBarValidation.RemoveFutureDatedBars(bars, out var removed, today: Today);

        removed.Should().Be(2);
        filtered.Should().HaveCount(2);
        filtered.Select(b => b.SessionDate).Should().Equal(Today.AddDays(-1), Today.AddDays(1));
    }
}
