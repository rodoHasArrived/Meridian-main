using FluentAssertions;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Storage.Backfill;
using Xunit;

namespace Meridian.Tests.Storage.Backfill;

public sealed class BackfillCoverageTests : IDisposable
{
    private readonly string _dataRoot = Path.Combine(
        Path.GetTempPath(), "meridian-backfill-coverage-store", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dataRoot))
            Directory.Delete(_dataRoot, recursive: true);
    }

    private static DateOnly D(int month, int day) => new(2024, month, day);

    private static BackfillCoverageRange R(DateOnly from, DateOnly to, long bars = 1) => new(from, to, bars);

    [Fact]
    public void Missing_WithoutCoverage_IsTheWholeRequest()
        => BackfillCoverageMath.Missing(D(1, 1), D(1, 31), []).Should().Equal((D(1, 1), D(1, 31)));

    [Fact]
    public void Missing_ReturnsLeadingInternalAndTrailingGaps()
    {
        var missing = BackfillCoverageMath.Missing(D(1, 1), D(3, 31), [R(D(2, 10), D(2, 20)), R(D(1, 10), D(1, 20))]);

        missing.Should().Equal(
            (D(1, 1), D(1, 9)),
            (D(1, 21), D(2, 9)),
            (D(2, 21), D(3, 31)));
    }

    [Fact]
    public void Missing_WhenFullyCovered_IsEmpty()
        => BackfillCoverageMath.Missing(D(1, 5), D(1, 20), [R(D(1, 1), D(1, 31))]).Should().BeEmpty();

    [Fact]
    public void Missing_OpenEndedRequest_AlwaysAsksForDatesAfterTheNewestCoverage()
        => BackfillCoverageMath.Missing(D(1, 1), null, [R(D(1, 1), D(1, 31))]).Should().Equal((D(2, 1), (DateOnly?)null));

    [Fact]
    public void Missing_RequestWithoutLowerBound_IsSatisfiedOnlyByOpenStartCoverage()
    {
        BackfillCoverageMath.Missing(null, D(1, 31), [R(D(1, 10), D(1, 31))])
            .Should().Equal(((DateOnly?)null, D(1, 9)));
        BackfillCoverageMath.Missing(null, D(1, 31), [R(BackfillCoverageRange.OpenStart, D(1, 31))])
            .Should().BeEmpty();
        BackfillCoverageMath.Missing(null, D(1, 31), [])
            .Should().Equal(((DateOnly?)null, D(1, 31)));
    }

    [Fact]
    public void Merge_JoinsOverlappingAndAdjacentRangesAndSumsBars()
    {
        var merged = BackfillCoverageMath.Merge([R(D(1, 11), D(1, 20), 8), R(D(1, 1), D(1, 10), 7), R(D(2, 1), D(2, 2), 2)]);

        merged.Should().Equal(R(D(1, 1), D(1, 20), 15), R(D(2, 1), D(2, 2), 2));
    }

    [Fact]
    public void WrittenRange_EndsAtTheNewestBarUnlessLaterDatesAreAlreadyCovered()
    {
        // Requested Monday 4 to Sunday 17 March; bars exist through Friday 15 March.
        BackfillCoverageMath.WrittenRange(D(3, 4), D(3, 17), D(3, 4), D(3, 15), 10, followedByCoverage: false)
            .Should().Be(R(D(3, 4), D(3, 15), 10));
        BackfillCoverageMath.WrittenRange(D(3, 4), D(3, 17), D(3, 4), D(3, 15), 10, followedByCoverage: true)
            .Should().Be(R(D(3, 4), D(3, 17), 10));
        // A long empty tail is never assumed, even before existing coverage.
        BackfillCoverageMath.WrittenRange(D(3, 4), D(3, 31), D(3, 4), D(3, 15), 10, followedByCoverage: true)
            .Should().Be(R(D(3, 4), D(3, 15), 10));
    }

    [Fact]
    public void WrittenRange_StartsAtTheRequestOnlyWhenTheFirstBarFollowsClosely()
    {
        BackfillCoverageMath.WrittenRange(D(3, 2), D(3, 15), D(3, 4), D(3, 15), 10, followedByCoverage: false)
            .Should().Be(R(D(3, 2), D(3, 15), 10));
        BackfillCoverageMath.WrittenRange(D(1, 1), D(3, 15), D(3, 4), D(3, 15), 10, followedByCoverage: false)
            .Should().Be(R(D(3, 4), D(3, 15), 10), "a provider that starts two months late did not cover January");
        BackfillCoverageMath.WrittenRange(null, D(3, 15), D(3, 4), D(3, 15), 10, followedByCoverage: false)
            .Should().Be(R(BackfillCoverageRange.OpenStart, D(3, 15), 10));
    }

    [Fact]
    public void WrittenRange_WithoutBars_CountsOnlyAShortGapBeforeExistingCoverage()
    {
        BackfillCoverageMath.WrittenRange(D(3, 16), D(3, 17), null, null, 0, followedByCoverage: true)
            .Should().Be(R(D(3, 16), D(3, 17), 0));
        BackfillCoverageMath.WrittenRange(D(3, 16), D(3, 17), null, null, 0, followedByCoverage: false).Should().BeNull();
        BackfillCoverageMath.WrittenRange(D(3, 1), D(3, 17), null, null, 0, followedByCoverage: true).Should().BeNull();
    }

    [Fact]
    public async Task Store_RoundTripsAndMergesCoveragePerSymbolProviderAndFeed()
    {
        var store = new BackfillStatusStore(_dataRoot);
        store.TryReadSymbolCoverage(DataGranularity.Daily, "alpaca", "iex:all").Should().BeNull();

        await store.WriteSymbolCoverageAsync(DataGranularity.Daily, "Alpaca", "IEX:all",
            [new BackfillCoverageUpdate("spy", [R(D(1, 2), D(1, 31), 21)], ["alpaca"])]);
        await store.WriteSymbolCoverageAsync(DataGranularity.Daily, "alpaca", "iex:all",
            [new BackfillCoverageUpdate("SPY", [R(D(2, 1), D(2, 29), 20)], ["alpaca"])]);
        await store.WriteSymbolCoverageAsync(DataGranularity.Daily, "alpaca", "sip:all",
            [new BackfillCoverageUpdate("SPY", [R(D(3, 1), D(3, 29), 20)], ["alpaca"])]);
        await store.WriteSymbolCoverageAsync(DataGranularity.Minute1, "alpaca", "iex:all",
            [new BackfillCoverageUpdate("SPY", [R(D(4, 1), D(4, 2), 780)], ["alpaca"])]);

        // A new store instance reads what was persisted.
        var reopened = new BackfillStatusStore(_dataRoot);
        reopened.TryReadSymbolCoverage(DataGranularity.Daily, "alpaca", "iex:all")!["spy"]
            .Should().Equal(R(D(1, 2), D(2, 29), 41));
        reopened.TryReadSymbolCoverage(DataGranularity.Daily, "alpaca", "sip:all")!["SPY"]
            .Should().Equal(R(D(3, 1), D(3, 29), 20));
        reopened.TryReadSymbolCoverage(DataGranularity.Minute1, "alpaca", "iex:all")!["SPY"]
            .Should().Equal(R(D(4, 1), D(4, 2), 780));
        reopened.TryReadSymbolCoverage(DataGranularity.Daily, "stooq", "default").Should().BeNull();
    }

    [Fact]
    public async Task Store_ClearByGranularity_RemovesOnlyThatLanesCoverage()
    {
        var store = new BackfillStatusStore(_dataRoot);
        await store.WriteSymbolCoverageAsync(DataGranularity.Daily, "alpaca", "default",
            [new BackfillCoverageUpdate("SPY", [R(D(1, 2), D(1, 31))], ["alpaca"])]);
        await store.WriteSymbolCoverageAsync(DataGranularity.Minute1, "alpaca", "default",
            [new BackfillCoverageUpdate("SPY", [R(D(1, 2), D(1, 3))], ["alpaca"])]);

        await store.ClearSymbolCheckpointsAsync(DataGranularity.Daily);

        store.TryReadSymbolCoverage(DataGranularity.Daily, "alpaca", "default").Should().BeNull();
        store.TryReadSymbolCoverage(DataGranularity.Minute1, "alpaca", "default").Should().ContainKey("SPY");

        await store.ClearSymbolCheckpointsAsync();
        store.TryReadSymbolCoverage(DataGranularity.Minute1, "alpaca", "default").Should().BeNull();
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"version\":99,\"entries\":[{\"symbol\":\"SPY\",\"granularity\":\"daily\",\"provider\":\"alpaca\",\"feed\":\"default\",\"ranges\":[{\"from\":\"2024-01-02\",\"to\":\"2024-01-31\",\"bars\":21}]}]}")]
    public async Task Store_UnreadableOrNewerSidecar_ProvesNoCoverage(string content)
    {
        var store = new BackfillStatusStore(_dataRoot);
        var statusDirectory = Path.Combine(_dataRoot, "_status");
        Directory.CreateDirectory(statusDirectory);
        await File.WriteAllTextAsync(Path.Combine(statusDirectory, "backfill-symbol-coverage.json"), content);

        store.TryReadSymbolCoverage(DataGranularity.Daily, "alpaca", "default").Should().BeNull();
    }
}
