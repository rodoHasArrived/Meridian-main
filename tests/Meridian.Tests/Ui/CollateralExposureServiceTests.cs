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
            buffer.IngestBatch(Scope, [Row(index)]);
        }

        // The window is bounded but ingestion never refuses. Exposure is an aggregate of what is
        // buffered, so a full buffer has to drop its oldest reading rather than turn away the newest:
        // refusing past capacity would freeze exposure at whatever the deployment happened to see
        // first and leave every later collateral movement unreported until a restart.
        buffer.BufferedCount(Scope).Should().Be(20_000);

        var counterparties = buffer.SnapshotCurrent(Scope).Select(row => row.Counterparty).ToArray();
        counterparties.Should().HaveCount(20_000);
        counterparties[^1].Should().Be("CPTY-20049", "the newest reading is the one exposure is about");
        counterparties[0].Should().Be("CPTY-50");
        counterparties.Should().NotContain("CPTY-0", "the oldest rows are what a full window gives up");
    }

    [Fact]
    public void CollateralIngestionBuffer_SnapshotCurrent_ReportsEveryRetainedExposure()
    {
        var buffer = new CollateralIngestionBuffer();

        for (var index = 0; index < 5_010; index++)
        {
            buffer.IngestBatch(Scope, [Row(index)]);
        }

        // Every retained row is a live exposure and BuildSnapshots treats its input as the complete
        // set, so a read limit below the window is a silent truncation: counterparties past it would
        // vanish from net exposure, coverage and breach evaluation with nothing to signal it. The read
        // is bounded by retention and by nothing else.
        var snapshot = buffer.SnapshotCurrent(Scope);

        snapshot.Should().HaveCount(5_010);
        snapshot[^1].Counterparty.Should().Be("CPTY-5009");
        snapshot[0].Counterparty.Should().Be("CPTY-0");
    }

    [Fact]
    public void CollateralIngestionBuffer_SnapshotCurrent_DoesNotConsumeWhatItReports()
    {
        var buffer = new CollateralIngestionBuffer();
        buffer.IngestBatch(Scope, [Row(1)]);

        buffer.SnapshotCurrent(Scope).Should().ContainSingle();
        buffer.SnapshotCurrent(Scope).Should().ContainSingle(
            "two operators looking at the same moment must see the same exposure");
        buffer.BufferedCount(Scope).Should().Be(1, "reading exposure is not a consumption of collateral input");
    }

    [Fact]
    public void CollateralIngestionBuffer_RestatingAnExposure_ReplacesItRatherThanAddingToIt()
    {
        var buffer = new CollateralIngestionBuffer();

        // A producer posting periodic refreshes for the same exposure. BuildSnapshots sums what it is
        // handed, so retaining both observations would report twice the exposure, and the figure would
        // keep climbing for as long as the producer kept refreshing.
        var observation = new CollateralInputRow(
            DateTimeOffset.UnixEpoch, "CPTY-A", "repo", 1_000m, 500m, 400m, "cash", 100m, 50m);
        buffer.IngestBatch(Scope, [observation]);
        buffer.IngestBatch(Scope, [observation with { AsOf = DateTimeOffset.UnixEpoch.AddMinutes(1) }]);

        buffer.BufferedCount(Scope).Should().Be(1, "the second delivery restates the first, it does not add to it");

        var snapshots = new CollateralExposureService().BuildSnapshots(buffer.SnapshotCurrent(Scope));
        snapshots.Should().ContainSingle();
        snapshots[0].GrossExposure.Should().Be(500m, "exposure is the current reading, not the sum of readings");
        snapshots[0].CollateralBalance.Should().Be(400m);
        snapshots[0].RequiredCollateral.Should().Be(150m);
    }

    [Fact]
    public void CollateralIngestionBuffer_RestatingOneExposure_LeavesTheOthersStanding()
    {
        var buffer = new CollateralIngestionBuffer();
        buffer.IngestBatch(Scope, [
            new CollateralInputRow(DateTimeOffset.UnixEpoch, "CPTY-A", "repo", 1m, 10m, 1m, "cash", 1m, 0m),
            new CollateralInputRow(DateTimeOffset.UnixEpoch, "CPTY-B", "swap", 1m, 20m, 1m, "cash", 1m, 0m)
        ]);

        // A delivery is not a full-picture reset: refreshing one counterparty must not erase another
        // the producer had no reason to resend.
        buffer.IngestBatch(Scope, [
            new CollateralInputRow(DateTimeOffset.UnixEpoch.AddMinutes(1), "CPTY-A", "repo", 1m, 30m, 1m, "cash", 1m, 0m)
        ]);

        var snapshots = new CollateralExposureService().BuildSnapshots(buffer.SnapshotCurrent(Scope));
        snapshots.Should().HaveCount(2);
        snapshots.Single(s => s.Counterparty == "CPTY-A").GrossExposure.Should().Be(30m);
        snapshots.Single(s => s.Counterparty == "CPTY-B").GrossExposure.Should().Be(20m);
    }

    [Fact]
    public void CollateralIngestionBuffer_TwoPositionsSharingAnIdentityInOneDelivery_AreBothKept()
    {
        var buffer = new CollateralIngestionBuffer();

        // Within one delivery the rows are simultaneous positions, not restatements of each other.
        // Collapsing them would under-report exposure -- which is why the API takes a batch rather
        // than being called once per row.
        buffer.IngestBatch(Scope, [
            new CollateralInputRow(DateTimeOffset.UnixEpoch, "CPTY-A", "repo", 1m, 10m, 1m, "cash", 1m, 0m),
            new CollateralInputRow(DateTimeOffset.UnixEpoch, "CPTY-A", "repo", 1m, 15m, 1m, "cash", 1m, 0m)
        ]);

        buffer.BufferedCount(Scope).Should().Be(2);
        var snapshots = new CollateralExposureService().BuildSnapshots(buffer.SnapshotCurrent(Scope));
        snapshots.Should().ContainSingle();
        snapshots[0].GrossExposure.Should().Be(25m);
    }

    [Fact]
    public void CollateralIngestionBuffer_StaleRestatement_DoesNotDisplaceANewerObservation()
    {
        var buffer = new CollateralIngestionBuffer();
        var identity = new CollateralInputRow(
            DateTimeOffset.UnixEpoch, "CPTY-A", "repo", 1_000m, 500m, 400m, "cash", 100m, 50m);

        buffer.IngestBatch(Scope, [identity with { AsOf = DateTimeOffset.UnixEpoch.AddMinutes(5), MarkToMarket = 900m }]);

        // Arrival order is not observation order: a delayed retry can land after a newer refresh.
        // Taking the last arrival would regress exposure, coverage and breach state to a stale reading.
        buffer.IngestBatch(Scope, [identity with { AsOf = DateTimeOffset.UnixEpoch.AddMinutes(1), MarkToMarket = 100m }]);

        buffer.BufferedCount(Scope).Should().Be(1);
        var snapshots = new CollateralExposureService().BuildSnapshots(buffer.SnapshotCurrent(Scope));
        snapshots.Should().ContainSingle();
        snapshots[0].GrossExposure.Should().Be(900m, "the newer observation stands");
    }

    [Fact]
    public void CollateralIngestionBuffer_MixedDelivery_KeepsTheFreshHalfAndDropsTheStaleHalf()
    {
        var buffer = new CollateralIngestionBuffer();
        var later = DateTimeOffset.UnixEpoch.AddMinutes(5);
        buffer.IngestBatch(Scope, [
            new CollateralInputRow(later, "CPTY-A", "repo", 1m, 900m, 1m, "cash", 1m, 0m),
            new CollateralInputRow(DateTimeOffset.UnixEpoch, "CPTY-B", "swap", 1m, 20m, 1m, "cash", 1m, 0m)
        ]);

        // Staleness is decided per exposure, not per delivery: a batch restating one exposure with an
        // old reading and another with a new one keeps the new half rather than being dropped whole.
        buffer.IngestBatch(Scope, [
            new CollateralInputRow(DateTimeOffset.UnixEpoch.AddMinutes(1), "CPTY-A", "repo", 1m, 100m, 1m, "cash", 1m, 0m),
            new CollateralInputRow(later, "CPTY-B", "swap", 1m, 70m, 1m, "cash", 1m, 0m)
        ]);

        var snapshots = new CollateralExposureService().BuildSnapshots(buffer.SnapshotCurrent(Scope));
        snapshots.Should().HaveCount(2);
        snapshots.Single(x => x.Counterparty == "CPTY-A").GrossExposure.Should().Be(900m, "the stale restatement is ignored");
        snapshots.Single(x => x.Counterparty == "CPTY-B").GrossExposure.Should().Be(70m, "the fresh restatement is applied");
    }

    [Fact]
    public void CollateralIngestionBuffer_IdentitiesDifferingOnlyByFieldBoundary_DoNotEvictEachOther()
    {
        var buffer = new CollateralIngestionBuffer();

        // Joining the identity fields with a delimiter is not injective when a field may contain it:
        // ("A:B", "C") and ("A", "B:C") would share a key, and a delivery for either would evict the
        // other -- silently understating exposure. The ingest route accepts these values verbatim.
        buffer.IngestBatch(Scope, [
            new CollateralInputRow(DateTimeOffset.UnixEpoch, "A:B", "C", 1m, 10m, 1m, "cash", 1m, 0m)
        ]);
        buffer.IngestBatch(Scope, [
            new CollateralInputRow(DateTimeOffset.UnixEpoch, "A", "B:C", 1m, 20m, 1m, "cash", 1m, 0m)
        ]);

        buffer.BufferedCount(Scope).Should().Be(2, "these are two distinct exposures, not a restatement");
        var snapshots = new CollateralExposureService().BuildSnapshots(buffer.SnapshotCurrent(Scope));
        snapshots.Should().HaveCount(2);
        snapshots.Sum(x => x.GrossExposure).Should().Be(30m);
    }

    [Fact]
    public void HaircutRules_DifferingOnlyByFieldBoundary_ResolveIndependently()
    {
        // The haircut lookup joined its key the same way, two methods apart, so the same collision
        // would have applied one counterparty's haircut to another's collateral.
        var service = new CollateralExposureService();
        service.UpsertHaircutRule(new HaircutRule("CPTY:X", "govt", 0.50m));
        service.UpsertHaircutRule(new HaircutRule("CPTY", "X:govt", 0m));

        var snapshots = service.BuildSnapshots([
            new CollateralInputRow(DateTimeOffset.UnixEpoch, "CPTY:X", "repo", 1m, 1m, 100m, "govt", 50m, 0m)
        ]);

        snapshots.Should().ContainSingle();
        snapshots[0].HaircutAdjustedCollateral.Should().Be(50m, "the 50% rule for this counterparty applies, not the other pair's 0%");
    }

    [Fact]
    public void CollateralIngestionBuffer_BatchWithMixedTimestampsForOneIdentity_KeepsOnlyTheWinningObservation()
    {
        var buffer = new CollateralIngestionBuffer();
        buffer.IngestBatch(Scope, [
            new CollateralInputRow(DateTimeOffset.UnixEpoch.AddMinutes(5), "CPTY-A", "repo", 1m, 500m, 1m, "cash", 1m, 0m)
        ]);

        // A delivery carrying two readings for one identity at different times is a restatement plus a
        // straggler, not two simultaneous positions. Keeping both would fold the superseded reading
        // back into exposure, which is the bug the newest-wins rule exists to prevent -- the straggler
        // arriving inside the winning batch rather than in a later one does not change that.
        buffer.IngestBatch(Scope, [
            new CollateralInputRow(DateTimeOffset.UnixEpoch.AddMinutes(6), "CPTY-A", "repo", 1m, 700m, 1m, "cash", 1m, 0m),
            new CollateralInputRow(DateTimeOffset.UnixEpoch.AddMinutes(1), "CPTY-A", "repo", 1m, 100m, 1m, "cash", 1m, 0m)
        ]);

        buffer.BufferedCount(Scope).Should().Be(1);
        var snapshots = new CollateralExposureService().BuildSnapshots(buffer.SnapshotCurrent(Scope));
        snapshots.Should().ContainSingle();
        snapshots[0].GrossExposure.Should().Be(700m, "only the minute-6 reading survives");
    }

    [Fact]
    public void CollateralIngestionBuffer_IsPartitionedByTenantScope()
    {
        var buffer = new CollateralIngestionBuffer();
        var alpha = CollateralTenantScope.For("tenant-alpha", "company-a");
        var beta = CollateralTenantScope.For("tenant-beta", "company-b");

        // The buffer is a process-wide singleton. Without a scope key, one tenant's readings appear in
        // another's exposure, and a same-identity restatement from either overwrites the other's
        // current reading -- the counterparty name is the same in both books.
        buffer.IngestBatch(alpha, [
            new CollateralInputRow(DateTimeOffset.UnixEpoch, "CPTY-SHARED", "repo", 1m, 100m, 1m, "cash", 1m, 0m)
        ]);
        buffer.IngestBatch(beta, [
            new CollateralInputRow(DateTimeOffset.UnixEpoch.AddMinutes(9), "CPTY-SHARED", "repo", 1m, 900m, 1m, "cash", 1m, 0m)
        ]);

        buffer.BufferedCount(alpha).Should().Be(1);
        buffer.BufferedCount(beta).Should().Be(1);

        var service = new CollateralExposureService();
        service.BuildSnapshots(buffer.SnapshotCurrent(alpha))
            .Should().ContainSingle().Which.GrossExposure.Should().Be(100m,
                "the later, larger reading belongs to another tenant and must not displace or join this one");
        service.BuildSnapshots(buffer.SnapshotCurrent(beta))
            .Should().ContainSingle().Which.GrossExposure.Should().Be(900m);

        buffer.SnapshotCurrent(CollateralTenantScope.Unscoped).Should().BeEmpty(
            "a scope that ingested nothing reads nothing, rather than falling back to another tenant's rows");
    }

    [Fact]
    public void CollateralIngestionBuffer_AtCapacity_EvictsWholeObservationsRatherThanRows()
    {
        var buffer = new CollateralIngestionBuffer();

        // The oldest exposure is two simultaneous positions. BuildSnapshots treats whatever survives as
        // the complete current exposure, so trimming by row count could leave one of the pair standing
        // and silently understate that counterparty rather than reporting it as gone.
        buffer.IngestBatch(Scope, [
            new CollateralInputRow(DateTimeOffset.UnixEpoch, "CPTY-PAIR", "repo", 1m, 11m, 1m, "cash", 1m, 0m),
            new CollateralInputRow(DateTimeOffset.UnixEpoch, "CPTY-PAIR", "repo", 1m, 13m, 1m, "cash", 1m, 0m)
        ]);

        for (var index = 0; index < 20_000; index++)
        {
            buffer.IngestBatch(Scope, [Row(index)]);
        }

        var snapshots = new CollateralExposureService().BuildSnapshots(buffer.SnapshotCurrent(Scope));
        snapshots.Should().NotContain(
            x => x.Counterparty == "CPTY-PAIR",
            "the pair is evicted together, so the counterparty is absent rather than half-reported");
        buffer.BufferedCount(Scope).Should().BeLessThanOrEqualTo(20_000);
    }

    private static readonly CollateralTenantScope Scope = CollateralTenantScope.Unscoped;

    private static CollateralInputRow Row(int index)
        => new(DateTimeOffset.UnixEpoch.AddSeconds(index), $"CPTY-{index}", "repo", 1m, 1m, 1m, "cash", 1m, 0m);
}
