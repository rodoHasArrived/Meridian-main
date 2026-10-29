using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.SecurityMaster.Workbench;

/// <summary>
/// The merge side of the readable-before-writable terms expansion (ADR-024, priority 5, increment
/// 4a). The merge hands the complete merged document to the canonical amend seam, which maps it in
/// Write mode, so a revision approved before this node refused it at submission (a <c>legs</c>
/// replacement carrying a leg <c>scheduleConventions</c> block, or any edit of a row stamped above the
/// write version) fails at merge with the precise reason and persists nothing. That failure fails the
/// publish fan-out, which leaves the revision Approved: the in-flight drain the 4a deploy notes call
/// for, pinned end to end by the workbench publish tests. Unset leg blocks still merge, and nothing of
/// them is persisted at the write version. Increment 4b adds the callSchedule and putSchedule arrays
/// through the same Write-mode guard: a row stamped 3 fails by its stamp, and a stray populated array
/// on a row stamped below its version fails by the array's path.
/// </summary>
public sealed partial class ApprovedFieldEditCanonicalMergeHandlerTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task StagedLegsRevisionCarryingAPopulatedLegBlock_FailsAtMergeWithTheNotYetWritableReason(int legIndex)
    {
        // Approved before submission refused leg blocks: the merge rebuilds the complete swap with the
        // staged legs, and the amend seam's Write-mode mapping refuses the populated block by its path.
        SetProjection("Swap", AheadOfWriteFixtures.Parse(AheadOfWriteFixtures.SwapAtWriteVersion));
        var revisionId = await SeedApprovedFieldRevisionAsync(
            AheadOfWriteFixtures.LegsPath,
            AheadOfWriteFixtures.SwapLegsWith(legIndex, AheadOfWriteFixtures.PopulatedLegBlock));
        var canonical = new CanonicalAmendHarness(_projectionStore);

        // Dated inside the record's open window, as the unset-block twin below is, so the populated
        // block is the only thing that can refuse the amendment.
        var refused = await CreateCanonicalMergeHandler(canonical.Service)
            .Invoking(h => h.HandleAsync(Event(revisionId, AheadOfWriteFixtures.LegsPath) with { EffectiveFrom = DateTimeOffset.UtcNow }))
            .Should().ThrowAsync<InvalidOperationException>("a failed merge fails the publish fan-out");
        refused.Which.Message.Should().Be(
            AheadOfWriteFixtures.NotYetWritableReason("Swap", $"legs[{legIndex}].scheduleConventions", sinceVersion: 2, writes: 1));

        canonical.Appended.Should().BeEmpty("the refused amendment appends no economic event");
        canonical.Upserted.Should().BeEmpty("the refused amendment leaves the canonical projection untouched");
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("""{"paymentCalendars":[]}""")]
    [InlineData("""{"businessDayConvention":null,"rollConvention":null}""")]
    public async Task StagedLegsRevisionCarryingAnUnsetLegBlock_MergesAtTheWriteVersionWithoutTheBlock(string unsetBlock)
    {
        // Absent, null, [] and all-null objects mean unset: the merge goes through, and the version-1
        // writer persists no scheduleConventions key on any leg.
        SetProjection("Swap", AheadOfWriteFixtures.Parse(AheadOfWriteFixtures.SwapAtWriteVersion));
        var revisionId = await SeedApprovedFieldRevisionAsync(
            AheadOfWriteFixtures.LegsPath,
            AheadOfWriteFixtures.SwapLegsWith(legIndex: 1, unsetBlock));
        var canonical = new CanonicalAmendHarness(_projectionStore);

        await CreateCanonicalMergeHandler(canonical.Service)
            .HandleAsync(Event(revisionId, AheadOfWriteFixtures.LegsPath) with { EffectiveFrom = DateTimeOffset.UtcNow });

        var persisted = JsonNode.Parse(canonical.Upserted.Should().ContainSingle().Subject.AssetSpecificTerms.GetRawText())!.AsObject();
        persisted[SecurityTermsEnvelope.AssetClassTermsVersionKey]!.GetValue<int>().Should().Be(1);
        var legs = persisted["legs"]!.AsArray();
        legs.Should().HaveCount(2);
        legs.Should().OnlyContain(
            leg => !leg!.AsObject().ContainsKey("scheduleConventions"),
            "a writer below the block's version never emits its key");
        legs[1]!["spreadBps"]!.GetValue<decimal>().Should().Be(20m, "the approved leg change itself merges");
    }

    [Fact]
    public async Task ApprovedEditOnABondWithContradictoryRoll_FailsAtMerge()
    {
        // A row a newer node wrote at version 2: merging an edit re-submits its stamp, which this
        // node reads but does not write.
        SetProjection("Bond", AheadOfWriteFixtures.BondTerms(stamp: 2, withBlock: true));
        var revisionId = await SeedApprovedFieldRevisionAsync("assetSpecificTerms.issuerName", "ACME Holdings");
        var canonical = new CanonicalAmendHarness(_projectionStore);

        // Dated inside the record's open window, so the stamp is the only thing that can refuse it.
        var refused = await CreateCanonicalMergeHandler(canonical.Service)
            .Invoking(h => h.HandleAsync(Event(revisionId, "assetSpecificTerms.issuerName") with { EffectiveFrom = DateTimeOffset.UtcNow }))
            .Should().ThrowAsync<InvalidOperationException>();
        refused.Which.Message.Should().Be("Invalid declared schedule terms: Declared rollConvention 'EndOfMonth' conflicts with endOfMonthRule false.");

        canonical.Appended.Should().BeEmpty();
        canonical.Upserted.Should().BeEmpty();
    }

    [Fact]
    public async Task ApprovedEditOnABondWithMissingExerciseStyle_FailsAtMerge()
    {
        // A row a newer node wrote at version 3 with declared schedules (stamped by hand here: no node
        // writes version 3 yet). This node reads it since 4b but writes version 1, so merging an edit
        // re-submits a stamp it cannot write; the stamp refuses it before the arrays are looked at.
        SetProjection(
            "Bond",
            AheadOfWriteFixtures.BondTerms(
                stamp: 3, withBlock: false, AheadOfWriteFixtures.PopulatedCallSchedule, AheadOfWriteFixtures.PopulatedPutSchedule));
        var revisionId = await SeedApprovedFieldRevisionAsync("assetSpecificTerms.issuerName", "ACME Holdings");
        var canonical = new CanonicalAmendHarness(_projectionStore);

        // Dated inside the record's open window, so the stamp is the only thing that can refuse it.
        var refused = await CreateCanonicalMergeHandler(canonical.Service)
            .Invoking(h => h.HandleAsync(Event(revisionId, "assetSpecificTerms.issuerName") with { EffectiveFrom = DateTimeOffset.UtcNow }))
            .Should().ThrowAsync<InvalidOperationException>("a failed merge fails the publish fan-out");
        refused.Which.Message.Should().Contain("exerciseStyle must be explicitly stated");

        canonical.Appended.Should().BeEmpty("the refused amendment appends no economic event");
        canonical.Upserted.Should().BeEmpty("the refused amendment leaves the canonical projection untouched");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(null)]
    public async Task ApprovedEditOnABondCarryingAStrayCallSchedule_FailsAtMergeWithoutImplicitActivation(int? stamp)
    {
        // Approved before submission refused the row (a 4a node staged it, since the key was
        // undeclared there): the merged document still carries the populated callSchedule, a version-3
        // term this node reads but does not write, so the amend seam refuses it by its path instead of
        // persisting it or dropping it.
        SetProjection(
            "Bond", AheadOfWriteFixtures.BondTerms(stamp, withBlock: false, callSchedule: AheadOfWriteFixtures.StrayCallSchedule));
        var revisionId = await SeedApprovedFieldRevisionAsync("assetSpecificTerms.issuerName", "ACME Holdings");
        var canonical = new CanonicalAmendHarness(_projectionStore);

        // Dated inside the record's open window, so the array is the only thing that can refuse it.
        var refused = await CreateCanonicalMergeHandler(canonical.Service)
            .Invoking(h => h.HandleAsync(Event(revisionId, "assetSpecificTerms.issuerName") with { EffectiveFrom = DateTimeOffset.UtcNow }))
            .Should().ThrowAsync<InvalidOperationException>("a failed merge fails the publish fan-out");
        refused.Which.Message.Should().Be(
            AheadOfWriteFixtures.StrayContainerReason("callSchedule", 3));

        canonical.Appended.Should().BeEmpty("the refused amendment appends no economic event");
        canonical.Upserted.Should().BeEmpty("the refused amendment leaves the canonical projection untouched");
    }

    private ApprovedFieldEditCanonicalMergeHandler CreateCanonicalMergeHandler(ISecurityMasterAmender amender)
        => new(
            _revisions,
            NullLogger<ApprovedFieldEditCanonicalMergeHandler>.Instance,
            _projectionStore.Object,
            amender);
}
