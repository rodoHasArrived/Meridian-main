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

    [Fact]
    public void BuildSnapshots_NegligibleRequirementAgainstLargeCollateral_SaturatesInsteadOfOverflowing()
    {
        // The quotient is the one figure the ingest value cap cannot bound. A requirement small
        // enough against posted collateral drives it past decimal's range, and because the buffer is
        // non-consuming the row that caused it would stay current and take every later exposure read
        // for that tenant down with it.
        var rows = new[]
        {
            new CollateralInputRow(
                DateTimeOffset.UnixEpoch,
                "CPTY-SATURATE",
                "repo",
                PositionNotional: 1m,
                MarkToMarket: 1m,
                CollateralBalance: 1_000_000_000_000_000_000_000m,
                CollateralType: "bond",
                InitialMargin: 0.0000000001m,
                VariationMargin: 0m)
        };

        var snapshot = new CollateralExposureService().BuildSnapshots(rows).Should().ContainSingle().Subject;

        snapshot.CollateralCoverageRatio.Should().Be(
            999m,
            "coverage above the ceiling classifies the same as the zero-requirement case against thresholds near 1.0");
    }

    [Fact]
    public void CollateralIngestionBuffer_ObservationSplitAcrossRequests_KeepsEveryChunk()
    {
        // The ingest route caps a single request, so an observation with more rows than that cap has
        // to arrive in pieces. Replacing on an equal AsOf discarded the earlier pieces and left the
        // snapshot reporting only the last -- silently short, which is worse than visibly missing
        // because the number still looks like an exposure.
        var buffer = new CollateralIngestionBuffer();
        var asOf = DateTimeOffset.UnixEpoch;

        buffer.IngestBatch(Scope, [
            new CollateralInputRow(asOf, "CPTY-SPLIT", "repo", 1m, 10m, 1m, "cash", 1m, 0m),
            new CollateralInputRow(asOf, "CPTY-SPLIT", "repo", 1m, 20m, 1m, "cash", 1m, 0m)
        ]);
        buffer.IngestBatch(Scope, [
            new CollateralInputRow(asOf, "CPTY-SPLIT", "repo", 1m, 30m, 1m, "cash", 1m, 0m)
        ]);

        var snapshot = new CollateralExposureService()
            .BuildSnapshots(buffer.SnapshotCurrent(Scope))
            .Should().ContainSingle().Subject;

        snapshot.NetExposure.Should().Be(60m, "every chunk of one observation is part of that observation");
    }

    [Fact]
    public void CollateralIngestionBuffer_RedeliveredChunk_DoesNotDoubleCount()
    {
        // The other thing a same-AsOf delivery can be. Continuing must not turn an at-least-once
        // producer's retry into twice the exposure, so incoming rows are matched one-for-one against
        // what is already held and only the unmatched remainder is added.
        var buffer = new CollateralIngestionBuffer();
        var asOf = DateTimeOffset.UnixEpoch;
        var chunk = new CollateralInputRow(asOf, "CPTY-RETRY", "repo", 1m, 10m, 1m, "cash", 1m, 0m);

        buffer.IngestBatch(Scope, [chunk]);
        buffer.IngestBatch(Scope, [chunk]);

        var snapshot = new CollateralExposureService()
            .BuildSnapshots(buffer.SnapshotCurrent(Scope))
            .Should().ContainSingle().Subject;

        snapshot.NetExposure.Should().Be(10m, "a redelivered chunk restates what is held rather than adding to it");
    }

    [Fact]
    public void CollateralIngestionBuffer_IdenticalSimultaneousPositions_AreBothRetained()
    {
        // The case the one-for-one match must not swallow: two identical positions reported in a
        // single delivery are two real positions, not a row and its retry.
        var buffer = new CollateralIngestionBuffer();
        var asOf = DateTimeOffset.UnixEpoch;
        var position = new CollateralInputRow(asOf, "CPTY-TWIN", "repo", 1m, 10m, 1m, "cash", 1m, 0m);

        buffer.IngestBatch(Scope, [position, position]);

        var snapshot = new CollateralExposureService()
            .BuildSnapshots(buffer.SnapshotCurrent(Scope))
            .Should().ContainSingle().Subject;

        snapshot.NetExposure.Should().Be(20m);
    }

    [Fact]
    public void CollateralIngestionBuffer_NamedChunks_KeepIdenticalPositionsInDifferentChunks()
    {
        // The case value-matching alone cannot decide: the same position reported in two chunks of one
        // observation is two positions, while the same position reported twice under one chunk name is
        // a retry. Naming the piece is what separates them.
        var buffer = new CollateralIngestionBuffer();
        var asOf = DateTimeOffset.UnixEpoch;

        buffer.IngestBatch(Scope, [
            new CollateralInputRow(asOf, "CPTY-CHUNKED", "repo", 1m, 10m, 1m, "cash", 1m, 0m, ChunkId: "page-1")
        ]);
        buffer.IngestBatch(Scope, [
            new CollateralInputRow(asOf, "CPTY-CHUNKED", "repo", 1m, 10m, 1m, "cash", 1m, 0m, ChunkId: "page-2")
        ]);

        var service = new CollateralExposureService();
        service.BuildSnapshots(buffer.SnapshotCurrent(Scope))
            .Should().ContainSingle().Subject
            .NetExposure.Should().Be(20m, "different chunks of one observation carry different positions");

        // And the retry of a named chunk replaces itself rather than adding.
        buffer.IngestBatch(Scope, [
            new CollateralInputRow(asOf, "CPTY-CHUNKED", "repo", 1m, 10m, 1m, "cash", 1m, 0m, ChunkId: "page-2")
        ]);

        service.BuildSnapshots(buffer.SnapshotCurrent(Scope))
            .Should().ContainSingle().Subject
            .NetExposure.Should().Be(20m, "a chunk redelivered under the same name replaces itself");
    }

    [Fact]
    public void BuildSnapshots_NegativeCollateralAgainstNegligibleRequirement_SaturatesInsteadOfOverflowing()
    {
        // The other side of the coverage quotient. Collateral posted against the desk is an
        // operational absurdity, but it must report as a hard breach rather than take the tenant's
        // exposure read down with an OverflowException.
        var rows = new[]
        {
            new CollateralInputRow(
                DateTimeOffset.UnixEpoch,
                "CPTY-NEGATIVE",
                "repo",
                PositionNotional: 1m,
                MarkToMarket: 1m,
                CollateralBalance: -1_000_000_000_000_000_000_000m,
                CollateralType: "bond",
                InitialMargin: 0.0000000001m,
                VariationMargin: 0m)
        };

        var snapshot = new CollateralExposureService().BuildSnapshots(rows).Should().ContainSingle().Subject;

        snapshot.CollateralCoverageRatio.Should().Be(-999m);
    }

    [Fact]
    public void CollateralIngestionBuffer_UnnamedRetryDifferingOnlyInCasing_DoesNotDoubleCount()
    {
        // ExposureKey and BuildSnapshots fold identity casing, so ACME and acme are one counterparty.
        // Record equality does not, so matching a retry by the raw row would miss it, append the row,
        // and double that counterparty's exposure, collateral and margin.
        var buffer = new CollateralIngestionBuffer();
        var asOf = DateTimeOffset.UnixEpoch;

        buffer.IngestBatch(Scope, [
            new CollateralInputRow(asOf, "ACME", "repo", 1m, 10m, 1m, "cash", 1m, 0m)
        ]);
        buffer.IngestBatch(Scope, [
            new CollateralInputRow(asOf, "acme", "repo", 1m, 10m, 1m, "cash", 1m, 0m)
        ]);

        var snapshot = new CollateralExposureService()
            .BuildSnapshots(buffer.SnapshotCurrent(Scope))
            .Should().ContainSingle().Subject;

        snapshot.NetExposure.Should().Be(10m, "a retry is the same row whichever casing it arrives in");
    }

    [Fact]
    public void CollateralIngestionBuffer_StragglerChunk_DoesNotDeleteTheChunkItSharesAnIdentityWith()
    {
        // A delivery may restate one identity at the current time in one chunk and carry a straggler
        // for the same identity at an older time in another. The straggler is dropped -- only the
        // winning observation survives -- so treating its chunk as redelivered deletes what is held
        // under that name and puts nothing back: the chunk is replaced by a row that never lands.
        var buffer = new CollateralIngestionBuffer();
        var current = DateTimeOffset.UnixEpoch.AddMinutes(10);
        var stale = DateTimeOffset.UnixEpoch.AddMinutes(5);

        buffer.IngestBatch(Scope, [
            new CollateralInputRow(current, "CPTY-STRAGGLER", "repo", 1m, 10m, 1m, "cash", 1m, 0m, ChunkId: "page-1")
        ]);

        buffer.IngestBatch(Scope, [
            new CollateralInputRow(current, "CPTY-STRAGGLER", "repo", 1m, 10m, 1m, "cash", 1m, 0m, ChunkId: "page-2"),
            new CollateralInputRow(stale, "CPTY-STRAGGLER", "repo", 1m, 10m, 1m, "cash", 1m, 0m, ChunkId: "page-1")
        ]);

        var service = new CollateralExposureService();
        service.BuildSnapshots(buffer.SnapshotCurrent(Scope))
            .Should().ContainSingle().Subject
            .NetExposure.Should().Be(
                20m,
                "page-1 was retained at the current time and the straggler naming it is older, so it is neither replaced nor dropped");
    }

    [Fact]
    public void BuildSnapshots_CounterpartyDifferingOnlyByPadding_IsOneExposureUnderOnePolicy()
    {
        // Every key here compares case-insensitively but not whitespace-insensitively, so a producer
        // that pads one delivery and not the next split one counterparty into two exposures -- each
        // carrying half the position, each falling through to the default policy and missing the
        // haircut rule, and neither showing the desk its real coverage.
        var service = new CollateralExposureService();
        service.UpsertHaircutRule(new HaircutRule("CPTY-PADDED", "govt-bond", 0.50m));
        service.UpsertThresholdPolicy(new CounterpartyThresholdPolicy("CPTY-PADDED", 1.15m, 1.00m));

        var snapshot = service.BuildSnapshots([
            new CollateralInputRow(DateTimeOffset.UnixEpoch, "CPTY-PADDED", "repo", 1m, 40m, 100m, "govt-bond", 50m, 0m),
            new CollateralInputRow(DateTimeOffset.UnixEpoch, "  CPTY-PADDED  ", " repo ", 1m, 60m, 100m, " govt-bond ", 50m, 0m)
        ]).Should().ContainSingle().Subject;

        snapshot.Counterparty.Should().Be("CPTY-PADDED", "padding is not part of a counterparty's name");
        snapshot.NetExposure.Should().Be(100m);
        snapshot.ProductDecomposition.Should().ContainSingle().Subject.ProductType.Should().Be("repo");
        snapshot.HaircutAdjustedCollateral.Should().Be(100m, "the padded row resolves the same haircut rule as the unpadded one");
        snapshot.CollateralCoverageRatio.Should().Be(1.00m);

        service.EvaluateBreaches([snapshot])
            .Should().ContainSingle().Subject
            .Severity.Should().Be(
                ThresholdSeverity.EarlyWarning,
                "the counterparty's own policy resolves, rather than falling through to the default");
    }

    [Fact]
    public void CollateralIngestionBuffer_RestatementDifferingOnlyByPadding_ReplacesRatherThanAdds()
    {
        // The buffer's exposure identity has to fold padding the same way the snapshot builder does.
        // Folding one and not the other means a padded restatement neither replaces the row it
        // restates nor is summed with it -- it is retained beside it, and the stale half stays until
        // eviction.
        var buffer = new CollateralIngestionBuffer();

        buffer.IngestBatch(Scope, [
            new CollateralInputRow(DateTimeOffset.UnixEpoch, "CPTY-PADDED", "repo", 1m, 10m, 1m, "cash", 1m, 0m)
        ]);
        buffer.IngestBatch(Scope, [
            new CollateralInputRow(DateTimeOffset.UnixEpoch.AddMinutes(1), " cpty-padded ", " repo ", 1m, 25m, 1m, " cash ", 1m, 0m)
        ]);

        new CollateralExposureService()
            .BuildSnapshots(buffer.SnapshotCurrent(Scope))
            .Should().ContainSingle().Subject
            .NetExposure.Should().Be(25m, "a padded restatement is a restatement of the same exposure");
    }

    private static readonly CollateralTenantScope Scope = CollateralTenantScope.Unscoped;

    private static CollateralInputRow Row(int index)
        => new(DateTimeOffset.UnixEpoch.AddSeconds(index), $"CPTY-{index}", "repo", 1m, 1m, 1m, "cash", 1m, 0m);
}
