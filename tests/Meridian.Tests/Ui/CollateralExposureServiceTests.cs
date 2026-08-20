using FluentAssertions;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Ui;

public sealed class CollateralExposureServiceTests
{
    [Fact]
    public void BuildSnapshots_ComputesHaircutAdjustedCoverageAndThresholdBreaches()
    {
        var service = new CollateralExposureService();
        service.UpsertHaircutRule(new HaircutRule("CPTY-A", "govt-bond", 0.10m));
        service.UpsertThresholdPolicy(new CounterpartyThresholdPolicy("CPTY-A", 1.15m, 1.0m));

        var snapshots = service.BuildSnapshots([
            new CollateralInputRow(DateTimeOffset.UtcNow, "CPTY-A", "repo", 1_000_000m, 120_000m, 100_000m, "govt-bond", 90_000m, 20_000m)
        ]);

        snapshots.Should().HaveCount(1);
        snapshots[0].HaircutAdjustedCollateral.Should().Be(90_000m);
        snapshots[0].CollateralCoverageRatio.Should().BeApproximately(0.818181m, 0.0001m);

        var breaches = service.EvaluateBreaches(snapshots);
        breaches.Should().ContainSingle();
        breaches[0].Severity.Should().Be(ThresholdSeverity.HardBreach);
    }

    [Fact]
    public void CollateralIngestionBuffer_PastCapacity_KeepsTheMostRecentRowsAndEvictsTheOldest()
    {
        var buffer = new CollateralIngestionBuffer();

        for (var index = 0; index < 20_050; index++)
        {
            buffer.Ingest(Row(index));
        }

        // The window is bounded but ingestion never refuses. Exposure is an aggregate of what is
        // buffered, so a full buffer has to drop its oldest reading rather than turn away the newest:
        // refusing past capacity would freeze exposure at whatever the deployment happened to see
        // first and leave every later collateral movement unreported until a restart.
        buffer.BufferedCount.Should().Be(20_000);

        var counterparties = buffer.SnapshotRows(20_000).Select(row => row.Counterparty).ToArray();
        counterparties.Should().HaveCount(20_000);
        counterparties[^1].Should().Be("CPTY-20049", "the newest reading is the one exposure is about");
        counterparties[0].Should().Be("CPTY-50");
        counterparties.Should().NotContain("CPTY-0", "the oldest rows are what a full window gives up");
    }

    [Fact]
    public void CollateralIngestionBuffer_SnapshotRows_ReportsArrivalsPastTheReadLimit()
    {
        var buffer = new CollateralIngestionBuffer();

        for (var index = 0; index < 5_010; index++)
        {
            buffer.Ingest(Row(index));
        }

        // The most recent rows, not the first buffered ones. Reading from the head would pin the
        // snapshot to the oldest 5,000 readings, so exposure would stop tracking current collateral
        // the moment the buffer exceeded the read limit -- stale from row 5,001, not from the cap.
        var snapshot = buffer.SnapshotRows(5_000);

        snapshot.Should().HaveCount(5_000);
        snapshot[^1].Counterparty.Should().Be("CPTY-5009");
        snapshot[0].Counterparty.Should().Be("CPTY-10");
    }

    [Fact]
    public void CollateralIngestionBuffer_SnapshotRows_WithNonPositiveLimit_ReadsNothing()
    {
        var buffer = new CollateralIngestionBuffer();
        buffer.Ingest(Row(1));

        buffer.SnapshotRows(0).Should().BeEmpty();
        buffer.SnapshotRows(-1).Should().BeEmpty();
        buffer.BufferedCount.Should().Be(1, "a read that returns nothing still must not discard input");
    }

    private static CollateralInputRow Row(int index)
        => new(DateTimeOffset.UnixEpoch.AddSeconds(index), $"CPTY-{index}", "repo", 1m, 1m, 1m, "cash", 1m, 0m);
}
