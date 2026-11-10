using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Instruments.AssetOperations;
using Meridian.Storage.AssetOperations;
using NSubstitute;

namespace Meridian.Tests.AssetOperations;

/// <summary>
/// The engine-version stamps asset operations writes, and what happens to a stamp that is only read.
/// The legacy bond loop is stamped <c>fixed-income-cash-flow-projection-v2</c>; a stored
/// <c>-v1</c> run keeps its stamp because no reader branches on it.
/// </summary>
[Trait("Category", "Unit")]
public sealed class AssetOperationsEngineVersionStampTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid SecurityId = Guid.Parse("8a0b1c2d-0000-4000-8000-0000000000e1");

    [Fact]
    public void Stamps_KeepTheirWireValues()
    {
        // The literals are the wire contract: stored runs, flows, timelines, ladder contributions and
        // forecast evidence carry them. Changing one is a new engine version, not a rename.
        AssetOperationsEngineVersions.LegacyBondCashFlowV1.Should().Be("fixed-income-cash-flow-projection-v1");
        AssetOperationsEngineVersions.LegacyBondCashFlow.Should().Be("fixed-income-cash-flow-projection-v2");
        AssetOperationsEngineVersions.DeclaredObligation.Should().Be("asset-obligation-declared-v3");
        AssetOperationsEngineVersions.DeclaredObligationWithoutRateEvidence.Should().Be("asset-obligation-declared-v2");
        AssetOperationsEngineVersions.ObligationTimeline.Should().Be("asset-terms-obligations-timeline-v1");
        AssetOperationsEngineVersions.DeclaredObligationTimeline.Should().Be("asset-terms-obligations-timeline-declared-v1");
        AssetObligationProjectionService.EngineVersion.Should().Be("asset-obligation-projection-v1");
    }

    [Fact]
    public void LegacyBondReferenceRun_IsStampedV2_OnTheRunEveryFlowAndTheTimeline()
    {
        var detail = AssetOperationsProjectionBuilder.FromBondReference(Reference(), Subject(), new BondCalculationContext(AsOf));

        detail.CashFlowProjectionRuns.Should().ContainSingle()
            .Which.EngineVersion.Should().Be("fixed-income-cash-flow-projection-v2");
        detail.ProjectedCashFlows.Should().HaveCount(5).And.OnlyContain(flow =>
            flow.ExtensionPayload!.Value.GetProperty("engineVersion").GetString() == "fixed-income-cash-flow-projection-v2");
        detail.TermsObligationsTimeline!.EngineVersion.Should().Be(
            "fixed-income-cash-flow-projection-v2+asset-terms-obligations-timeline-v1");
    }

    [Fact]
    public async Task PublishedProjection_WithAStoredV1Run_KeepsItsStampWhenRead()
    {
        // Stored evidence is not restamped: a publication written before the v2 stamp reads back with
        // the run stamp it was written with, and the timeline derived on read is composed from it.
        var current = AssetOperationsProjectionBuilder.FromBondReference(Reference(), Subject(), new BondCalculationContext(AsOf));
        var stored = current with
        {
            CashFlowProjectionRuns = [current.CashFlowProjectionRuns[0] with { EngineVersion = AssetOperationsEngineVersions.LegacyBondCashFlowV1 }],
            TermsObligationsTimeline = null
        };
        var store = Substitute.For<IAssetOperationsProjectionStore>();
        store.GetAsync(SecurityId, Arg.Any<CancellationToken>()).Returns(stored);

        var detail = await new AssetOperationsReadService(projectionStore: store).GetOperationsAsync(SecurityId);

        detail!.CashFlowProjectionRuns.Should().ContainSingle()
            .Which.EngineVersion.Should().Be("fixed-income-cash-flow-projection-v1");
        detail.ProjectedCashFlows.Should().HaveCount(5);
        detail.TermsObligationsTimeline!.EngineVersion.Should().Be(
            "fixed-income-cash-flow-projection-v1+asset-terms-obligations-timeline-v1");
        detail.TermsObligationsTimeline.Events.Should().Contain(row => row.EventKind == "Coupon");
    }

    private static AssetOperationSubjectDto Subject()
        => new(SecurityId, "Bond", "Stamp bond", "InternalCode:STAMP-1", ["Terms", "CashFlowProjection"]);

    private static BondReferenceDto Reference()
        => new(SecurityId, "Stamp bond", "USD", "Meridian Funding LLC", "Senior", "InternalCode:STAMP-1",
            new BondLifecycleDto(SecurityId, BondLifecycleStat.Active, new DateOnly(2026, 1, 15), null,
                new DateOnly(2028, 1, 15), false, 2, Par: 100m, PaymentFrequency: "SemiAnnual"),
            new BondAccrualConventionDto(SecurityId, "30/360", 2, "US", "Fixed", 4m, null, null, 2), 2);
}
