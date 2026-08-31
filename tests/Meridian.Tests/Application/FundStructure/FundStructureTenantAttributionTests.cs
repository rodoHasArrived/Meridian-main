using FluentAssertions;
using Meridian.Application.FundStructure;

namespace Meridian.Tests.Application.FundStructure;

/// <summary>
/// W9-GOV-008 criterion 2, backfill half. The fund-structure hierarchy carries no tenant column and
/// <c>fund_profile_tenancy</c> can attribute only fund profiles, so the stamp that a fail-closed
/// reader depends on has to be derived. These tests pin the derivation's two asymmetric rules —
/// ownership flows down, exclusivity is only inferred up when every attributed descendant agrees —
/// and, just as importantly, pin what it refuses to guess.
/// </summary>
public sealed class FundStructureTenantAttributionTests
{
    private static readonly Guid Organization = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid BusinessAlpha = Guid.Parse("00000000-0000-0000-0000-0000000000b1");
    private static readonly Guid BusinessBeta = Guid.Parse("00000000-0000-0000-0000-0000000000b2");
    private static readonly Guid FundAlpha = Guid.Parse("00000000-0000-0000-0000-0000000000f1");
    private static readonly Guid FundBeta = Guid.Parse("00000000-0000-0000-0000-0000000000f2");
    private static readonly Guid SleeveAlpha = Guid.Parse("00000000-0000-0000-0000-0000000000c1");
    private static readonly Guid VehicleAlpha = Guid.Parse("00000000-0000-0000-0000-0000000000d1");
    private static readonly Guid OrphanEntity = Guid.Parse("00000000-0000-0000-0000-0000000000e9");

    [Fact]
    public void Derive_FlowsOwnershipDownFromAnAttributedFund()
    {
        var result = FundStructureTenantAttribution.Derive(
            Graph(
                Nodes(
                    (FundAlpha, "Fund"),
                    (SleeveAlpha, "Sleeve"),
                    (VehicleAlpha, "Vehicle")),
                Edge(FundAlpha, SleeveAlpha),
                Edge(SleeveAlpha, VehicleAlpha)),
            Seeds((FundAlpha, "tenant-alpha")));

        // A sleeve of tenant-alpha's fund is tenant-alpha's, and so is anything under it.
        result.Attributions[FundAlpha].Should().Be("tenant-alpha");
        result.Attributions[SleeveAlpha].Should().Be("tenant-alpha");
        result.Attributions[VehicleAlpha].Should().Be("tenant-alpha");
        result.Quarantined.Should().BeEmpty();
    }

    [Fact]
    public void Derive_InfersAnAncestorOnlyWhenEveryAttributedDescendantAgrees()
    {
        var result = FundStructureTenantAttribution.Derive(
            Graph(
                Nodes(
                    (BusinessAlpha, "Business"),
                    (FundAlpha, "Fund"),
                    (FundBeta, "Fund")),
                Edge(BusinessAlpha, FundAlpha),
                Edge(BusinessAlpha, FundBeta)),
            Seeds((FundAlpha, "tenant-alpha"), (FundBeta, "tenant-alpha")));

        result.Attributions[BusinessAlpha].Should().Be("tenant-alpha");
        result.Quarantined.Should().BeEmpty();
    }

    [Fact]
    public void Derive_QuarantinesASharedAncestorRatherThanHandingItToOneClaimant()
    {
        var result = FundStructureTenantAttribution.Derive(
            Graph(
                Nodes(
                    (Organization, "Organization"),
                    (FundAlpha, "Fund"),
                    (FundBeta, "Fund")),
                Edge(Organization, FundAlpha),
                Edge(Organization, FundBeta)),
            Seeds((FundAlpha, "tenant-alpha"), (FundBeta, "tenant-beta")));

        // Attributing the organization to either tenant would hand that tenant the other's fund.
        result.Attributions.Should().NotContainKey(Organization);

        var quarantined = result.Quarantined.Should().ContainSingle().Subject;
        quarantined.NodeId.Should().Be(Organization);
        quarantined.NodeKind.Should().Be("Organization");
        quarantined.Reason.Should().Be(FundStructureTenantQuarantineReason.MixedOwnership);
        quarantined.CandidateTenantIds.Should().BeEquivalentTo(["tenant-alpha", "tenant-beta"]);

        // The funds themselves stay attributed: a shared parent does not un-own its children.
        result.Attributions[FundAlpha].Should().Be("tenant-alpha");
        result.Attributions[FundBeta].Should().Be("tenant-beta");
    }

    [Fact]
    public void Derive_QuarantinesANodeUnderTwoOwnersRatherThanPickingOne()
    {
        var result = FundStructureTenantAttribution.Derive(
            Graph(
                Nodes(
                    (FundAlpha, "Fund"),
                    (FundBeta, "Fund"),
                    (VehicleAlpha, "Vehicle")),
                Edge(FundAlpha, VehicleAlpha),
                Edge(FundBeta, VehicleAlpha)),
            Seeds((FundAlpha, "tenant-alpha"), (FundBeta, "tenant-beta")));

        var quarantined = result.Quarantined.Should().ContainSingle().Subject;
        quarantined.NodeId.Should().Be(VehicleAlpha);
        quarantined.Reason.Should().Be(FundStructureTenantQuarantineReason.MixedOwnership);
        quarantined.CandidateTenantIds.Should().BeEquivalentTo(["tenant-alpha", "tenant-beta"]);
    }

    [Fact]
    public void Derive_QuarantinesANodeTheRegistryCannotReachInEitherDirection()
    {
        var result = FundStructureTenantAttribution.Derive(
            Graph(
                Nodes(
                    (FundAlpha, "Fund"),
                    (OrphanEntity, "LegalEntity"))),
            Seeds((FundAlpha, "tenant-alpha")));

        var quarantined = result.Quarantined.Should().ContainSingle().Subject;
        quarantined.NodeId.Should().Be(OrphanEntity);
        quarantined.Reason.Should().Be(FundStructureTenantQuarantineReason.Underivable);
        quarantined.CandidateTenantIds.Should().BeEmpty();
    }

    [Fact]
    public void Derive_KeepsASeedAuthoritativeOverInheritance()
    {
        var result = FundStructureTenantAttribution.Derive(
            Graph(
                Nodes(
                    (BusinessAlpha, "Business"),
                    (FundAlpha, "Fund")),
                Edge(BusinessAlpha, FundAlpha)),
            Seeds((BusinessAlpha, "tenant-alpha"), (FundAlpha, "tenant-beta")));

        // The registry speaks directly about the fund; an inherited value must not overwrite it.
        result.Attributions[FundAlpha].Should().Be("tenant-beta");
        result.Attributions[BusinessAlpha].Should().Be("tenant-alpha");
    }

    [Fact]
    public void Derive_TerminatesOnACycleAndQuarantinesWhatTheCycleMakesAmbiguous()
    {
        var result = FundStructureTenantAttribution.Derive(
            Graph(
                Nodes(
                    (BusinessAlpha, "Business"),
                    (BusinessBeta, "Business"),
                    (FundAlpha, "Fund")),
                Edge(BusinessAlpha, BusinessBeta),
                Edge(BusinessBeta, BusinessAlpha),
                Edge(BusinessAlpha, FundAlpha)),
            Seeds((BusinessAlpha, "tenant-alpha"), (BusinessBeta, "tenant-beta")));

        // An operator-maintained graph can contain a cycle, so the derivation must reach a fixpoint
        // rather than recursing forever. Both seeds stand, because the registry speaks directly
        // about those nodes.
        result.Attributions[BusinessAlpha].Should().Be("tenant-alpha");
        result.Attributions[BusinessBeta].Should().Be("tenant-beta");

        // The fund is a real conflict, not an artefact: inside a cycle each business owns the other,
        // so both tenants genuinely reach the fund and neither is entitled to it.
        var quarantined = result.Quarantined.Should().ContainSingle().Subject;
        quarantined.NodeId.Should().Be(FundAlpha);
        quarantined.Reason.Should().Be(FundStructureTenantQuarantineReason.MixedOwnership);
        quarantined.CandidateTenantIds.Should().BeEquivalentTo(["tenant-alpha", "tenant-beta"]);
    }

    [Fact]
    public void Derive_IgnoresBlankSeedsRatherThanStampingTheGraphToAnEmptyTenant()
    {
        var result = FundStructureTenantAttribution.Derive(
            Graph(
                Nodes(
                    (FundAlpha, "Fund"),
                    (SleeveAlpha, "Sleeve")),
                Edge(FundAlpha, SleeveAlpha)),
            Seeds((FundAlpha, "   ")));

        result.Attributions.Should().BeEmpty();
        result.Quarantined.Should().HaveCount(2);
        result.Quarantined.Should().OnlyContain(
            entry => entry.Reason == FundStructureTenantQuarantineReason.Underivable);
    }

    [Fact]
    public void Derive_TrimsSeedsAndTreatsTenantIdentityCaseInsensitively()
    {
        var result = FundStructureTenantAttribution.Derive(
            Graph(
                Nodes(
                    (Organization, "Organization"),
                    (FundAlpha, "Fund"),
                    (FundBeta, "Fund")),
                Edge(Organization, FundAlpha),
                Edge(Organization, FundBeta)),
            Seeds((FundAlpha, " tenant-alpha "), (FundBeta, "TENANT-ALPHA")));

        // The same tenant spelled two ways is one claimant, not a shared ancestor.
        result.Attributions[FundAlpha].Should().Be("tenant-alpha");
        result.Attributions.Should().ContainKey(Organization);
        result.Quarantined.Should().BeEmpty();
    }

    private static FundStructureTenantAttributionGraph Graph(
        Dictionary<Guid, string> nodes,
        params FundStructureOwnershipEdge[] edges)
        => new(nodes, edges);

    private static Dictionary<Guid, string> Nodes(params (Guid NodeId, string Kind)[] nodes)
        => nodes.ToDictionary(node => node.NodeId, node => node.Kind);

    private static FundStructureOwnershipEdge Edge(Guid parent, Guid child) => new(parent, child);

    private static Dictionary<Guid, string> Seeds(params (Guid NodeId, string TenantId)[] seeds)
        => seeds.ToDictionary(seed => seed.NodeId, seed => seed.TenantId);
}
