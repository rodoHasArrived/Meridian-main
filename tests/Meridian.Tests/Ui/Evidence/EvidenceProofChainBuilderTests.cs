using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Evidence;
using Xunit;

namespace Meridian.Tests.Ui.Evidence;

/// <summary>
/// Guards the v0.18 proof-chain projection that compliance reviewers use to judge
/// whether an evidence packet covers every layer from source to audit. The failure
/// mode under guard: nodes silently routed to the wrong layer, coverage math
/// overstating completeness, or a blocked/stale layer not propagating to the chain
/// status — all of which would let an incomplete evidence packet look review-ready.
/// </summary>
public sealed class EvidenceProofChainBuilderTests
{
    private static readonly EvidenceSubjectDto Subject = new(
        SubjectId: "subj-1",
        SubjectKind: "report-pack",
        Label: "Proof chain subject",
        Workspace: "reporting",
        Route: null,
        PageTag: "evidence");

    private static readonly string[] AllLayerLabels =
    [
        "Source", "Normalization", "Reconciliation", "Capital accounts",
        "Ledger", "Close", "Reporting", "Delivery", "Audit"
    ];

    // One kind per layer that matches that layer's keyword list exactly.
    private static readonly string[] OneKindPerLayer =
    [
        "source", "mapping", "reconciliation", "capital-account",
        "ledger", "approval", "report", "delivery", "audit"
    ];

    [Fact]
    public void Build_NullArguments_Throw()
    {
        var nodesAct = () => EvidenceProofChainBuilder.Build(null!, Completeness());
        var completenessAct = () => EvidenceProofChainBuilder.Build(Array.Empty<EvidenceNodeDto>(), null!);

        nodesAct.Should().Throw<ArgumentNullException>();
        completenessAct.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Build_WithNoNodesAndNoRequirements_ReturnsMissingChainWithNineEmptyLayers()
    {
        var chain = EvidenceProofChainBuilder.Build(Array.Empty<EvidenceNodeDto>(), Completeness());

        chain.TotalLayerCount.Should().Be(9);
        chain.CoveredLayerCount.Should().Be(0);
        chain.CoveragePercent.Should().Be(0);
        chain.Status.Should().Be(EvidenceStatusDto.Missing);
        chain.Layers.Select(l => l.Label).Should().Equal(AllLayerLabels);
        chain.Layers.Should().OnlyContain(l => l.Status == EvidenceStatusDto.Missing);
        chain.Layers.Should().OnlyContain(l => l.Summary.StartsWith("No ") && l.Summary.EndsWith("evidence is present in this packet."));
    }

    [Fact]
    public void Build_WithReadyNodeInEveryLayer_ReturnsReadyChainAtFullCoverage()
    {
        var nodes = OneKindPerLayer
            .Select((kind, i) => Node($"n{i}", kind))
            .ToArray();
        var completeness = Completeness(ready: nodes.Select(n => n.EvidenceId).ToArray());

        var chain = EvidenceProofChainBuilder.Build(nodes, completeness);

        chain.Status.Should().Be(EvidenceStatusDto.Ready);
        chain.CoveredLayerCount.Should().Be(9);
        chain.CoveragePercent.Should().Be(100);
        chain.Layers.Should().OnlyContain(l => l.Status == EvidenceStatusDto.Ready);
        chain.Layers.Should().OnlyContain(l => l.CoveragePercent == 100);
    }

    [Fact]
    public void Build_KindMatchingExactKeyword_WinsOverSummaryText()
    {
        // The Summary screams "delivery", but the Kind is an exact ledger keyword.
        var node = Node("n1", "ledger", summary: "delivery portal audit trail");

        var chain = EvidenceProofChainBuilder.Build(new[] { node }, Completeness());

        LayerByLabel(chain, "Ledger").EvidenceIds.Should().ContainSingle().Which.Should().Be("n1");
        LayerByLabel(chain, "Delivery").EvidenceIds.Should().BeEmpty();
        LayerByLabel(chain, "Audit").EvidenceIds.Should().BeEmpty();
    }

    [Fact]
    public void Build_EvidenceIdSuffixKeyword_AssignsLayerFromIdSuffix()
    {
        var node = Node("x:y:approval", "widget");

        var chain = EvidenceProofChainBuilder.Build(new[] { node }, Completeness());

        LayerByLabel(chain, "Close").EvidenceIds.Should().ContainSingle().Which.Should().Be("x:y:approval");
    }

    [Fact]
    public void Build_ArtifactTextFallback_AssignsLayerAndKeywordFreeNodeLandsNowhere()
    {
        var artifactRouted = Node("n1", "widget", artifacts: new[]
        {
            new EvidenceArtifactRefDto("a1", "vault", Path: null, Route: null,
                GeneratedAt: DateTimeOffset.UtcNow, Hash: null, Retained: true)
        });
        var keywordFree = Node("n2", "widget");

        var chain = EvidenceProofChainBuilder.Build(new[] { artifactRouted, keywordFree }, Completeness());

        LayerByLabel(chain, "Audit").EvidenceIds.Should().ContainSingle().Which.Should().Be("n1");
        chain.Layers.SelectMany(l => l.EvidenceIds).Should().NotContain("n2",
            "a node with no layer keywords anywhere must not be force-assigned to a layer");
        chain.CoveredLayerCount.Should().Be(1);
    }

    [Fact]
    public void Build_RequiredIdAbsentFromNodes_MarksLayerBlockedAndChainBlocked()
    {
        var nodes = new[] { Node("n1", "ledger") };
        var completeness = Completeness(required: new[] { "delivery-manifest" }, ready: new[] { "n1" });

        var chain = EvidenceProofChainBuilder.Build(nodes, completeness);

        var delivery = LayerByLabel(chain, "Delivery");
        delivery.MissingEvidenceIds.Should().ContainSingle().Which.Should().Be("delivery-manifest");
        delivery.Status.Should().Be(EvidenceStatusDto.Blocked);
        chain.Status.Should().Be(EvidenceStatusDto.Blocked,
            "a required-but-absent evidence id must block the whole chain");
    }

    [Fact]
    public void Build_StaleNode_MarksChainStale_AndBlockedOutranksStale()
    {
        var readyLedger = Node("n1", "ledger");
        var staleReport = Node("n2", "report", isStale: true);

        var staleChain = EvidenceProofChainBuilder.Build(new[] { readyLedger, staleReport }, Completeness());
        staleChain.Status.Should().Be(EvidenceStatusDto.Stale);
        LayerByLabel(staleChain, "Reporting").Status.Should().Be(EvidenceStatusDto.Stale);

        var blockedNode = Node("n3", "delivery", status: EvidenceStatusDto.Blocked);
        var blockedChain = EvidenceProofChainBuilder.Build(
            new[] { readyLedger, staleReport, blockedNode }, Completeness());
        blockedChain.Status.Should().Be(EvidenceStatusDto.Blocked, "blocked outranks stale");
    }

    [Fact]
    public void Build_ReviewRequiredNode_ReturnsReviewRequiredChain()
    {
        var node = Node("n1", "ledger", status: EvidenceStatusDto.ReviewRequired);

        var chain = EvidenceProofChainBuilder.Build(new[] { node }, Completeness());

        chain.Status.Should().Be(EvidenceStatusDto.ReviewRequired);
        chain.Summary.Should().Contain("1 review-required");
    }

    [Fact]
    public void Build_WithFiveCoveredLayers_RoundsCoverageAwayFromZeroTo56()
    {
        var nodes = OneKindPerLayer
            .Take(5)
            .Select((kind, i) => Node($"n{i}", kind))
            .ToArray();

        var chain = EvidenceProofChainBuilder.Build(nodes, Completeness());

        chain.CoveredLayerCount.Should().Be(5);
        chain.CoveragePercent.Should().Be(56, "5/9 = 55.6% must round away from zero");
        chain.Summary.Should().StartWith("5 of 9 v0.18 proof-chain layers");
    }

    [Fact]
    public void Build_WithDuplicateAndUnsortedEvidenceIds_ReturnsDistinctOrderedIdLists()
    {
        var nodes = new[] { Node("B-entry", "ledger"), Node("a-entry", "ledger") };

        var chain = EvidenceProofChainBuilder.Build(nodes, Completeness());

        var ledger = LayerByLabel(chain, "Ledger");
        ledger.EvidenceIds.Should().Equal("a-entry", "B-entry");
        ledger.EvidenceKinds.Should().ContainSingle().Which.Should().Be("ledger");
    }

    [Fact]
    public void Build_WithRequiredIdsPresent_UsesRequiredCountAsCoverageDenominator()
    {
        var nodes = new[] { Node("r1", "ledger"), Node("r2", "ledger"), Node("extra", "ledger") };
        var completeness = Completeness(required: new[] { "r1", "r2" }, ready: new[] { "r1" });

        var chain = EvidenceProofChainBuilder.Build(nodes, completeness);

        LayerByLabel(chain, "Ledger").CoveragePercent.Should().Be(50,
            "coverage is measured against the 2 required ids, not the 3 present nodes");
    }

    private static EvidenceProofChainLayerDto LayerByLabel(EvidenceProofChainDto chain, string label) =>
        chain.Layers.Single(l => l.Label == label);

    private static EvidenceNodeDto Node(
        string evidenceId,
        string kind,
        EvidenceStatusDto status = EvidenceStatusDto.Ready,
        bool isStale = false,
        string summary = "n/a",
        IReadOnlyList<EvidenceArtifactRefDto>? artifacts = null) =>
        new(
            EvidenceId: evidenceId,
            Subject: Subject,
            Kind: kind,
            Status: status,
            Freshness: new EvidenceFreshnessDto(DateTimeOffset.UtcNow, isStale, null),
            SourceSystem: "Tests",
            Summary: summary,
            ArtifactRefs: artifacts ?? Array.Empty<EvidenceArtifactRefDto>(),
            RelatedWorkItemIds: Array.Empty<string>());

    private static EvidenceCompletenessDto Completeness(
        string[]? required = null,
        string[]? ready = null,
        string[]? missing = null) =>
        new(
            Score: 100,
            Status: EvidenceStatusDto.Ready,
            RequiredIds: required ?? Array.Empty<string>(),
            ReadyIds: ready ?? Array.Empty<string>(),
            MissingIds: missing ?? Array.Empty<string>(),
            StaleIds: Array.Empty<string>(),
            BlockingWorkItemIds: Array.Empty<string>());
}
