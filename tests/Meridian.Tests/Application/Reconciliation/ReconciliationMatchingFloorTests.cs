using FluentAssertions;
using Meridian.Domain.Reconciliation;
using Meridian.FinancialOperations.Reconciliation;

namespace Meridian.Tests.Application.Reconciliation;

/// <summary>
/// Pins the rebuilt matching floor of <see cref="ReconciliationMatchingEngine"/>: sided
/// populations (rows never match their own source), scored best-match assignment with stable
/// tie-breakers, one-to-many / many-to-one split groups recorded in <see cref="MatchEvidence"/>,
/// business-calendar-aware cash staging, fail-closed currency identity, and content-derived
/// artifact ids that make re-runs idempotent. Legacy staging semantics are pinned separately in
/// <see cref="CanonicalReconciliationMatchingEngineTests"/>.
/// </summary>
public sealed class ReconciliationMatchingFloorTests
{
    // 2026-05-28 is a Thursday; 2026-05-29 a Friday; 2026-06-01 the following Monday.
    private static readonly DateTimeOffset Thursday = new(2026, 5, 28, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Friday = new(2026, 5, 29, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Run_CashBestMatch_PrefersClosestAmountOverFirstCandidate()
    {
        var engine = new ReconciliationMatchingEngine();
        var profile = CashProfile(new CashToleranceRule("cash-abs-5-v1", 5m, null, TimeSpan.FromDays(1)));
        var run = CreateRun([
            CreateSnapshot("prime", ReconciliationSourceType.Prime, cash: [CreateCash("c1", 10000m, Thursday)]),
            CreateSnapshot("custodian", ReconciliationSourceType.Custodian, cash:
            [
                CreateCash("c2", 10004.9m, Thursday),
                CreateCash("c3", 10000.5m, Thursday)
            ])
        ]);

        var result = engine.Run(run, profile);

        var match = result.matches.Should().ContainSingle(m => m.Classification == BreakClassification.MatchedWithinTolerance).Subject;
        match.CashEntryIds.Should().BeEquivalentTo(["c1", "c3"], "the scored floor must pick the closest amount, not the first near-tolerance candidate");
        var breakRecord = result.breaks.Should().ContainSingle().Subject;
        var breakEvidence = result.evidence.Single(e => e.EvidenceId == breakRecord.EvidenceIds.Single());
        breakEvidence.Attributes["cashEntryId"].Should().Be("c2");
    }

    [Fact]
    public void Run_CashBestMatch_TieBreaksOnEntryIdOrdinal()
    {
        var engine = new ReconciliationMatchingEngine();
        var profile = CashProfile(new CashToleranceRule("cash-abs-5-v1", 5m, null, TimeSpan.FromDays(1)));
        var run = CreateRun([
            CreateSnapshot("prime", ReconciliationSourceType.Prime, cash: [CreateCash("c1", 1000m, Thursday)]),
            CreateSnapshot("custodian", ReconciliationSourceType.Custodian, cash:
            [
                CreateCash("cB", 1000.5m, Thursday),
                CreateCash("cA", 1000.5m, Thursday)
            ])
        ]);

        var result = engine.Run(run, profile);

        var match = result.matches.Should().ContainSingle(m => m.Classification == BreakClassification.MatchedWithinTolerance).Subject;
        match.CashEntryIds.Should().BeEquivalentTo(["c1", "cA"], "equal scores must tie-break on ordinal entry id, deterministically");
    }

    [Fact]
    public void Run_CashEntriesInSameSnapshot_NeverMatch()
    {
        var engine = new ReconciliationMatchingEngine();
        var profile = CashProfile(new CashToleranceRule("cash-abs-5-v1", 5m, null, TimeSpan.FromDays(1)));
        var run = CreateRun([
            CreateSnapshot("prime", ReconciliationSourceType.Prime, cash:
            [
                CreateCash("c1", 1000m, Thursday),
                CreateCash("c2", 1000m, Thursday)
            ])
        ]);

        var result = engine.Run(run, profile);

        result.matches.Should().BeEmpty("a source snapshot must never reconcile against itself");
        result.breaks.Should().HaveCount(2);
    }

    [Fact]
    public void Run_CashEqualAmountsSamePeriod_MatchOnExactStage()
    {
        var engine = new ReconciliationMatchingEngine();
        var profile = CashProfile(new CashToleranceRule("cash-abs-5-v1", 5m, null, TimeSpan.FromDays(1)));
        var run = CreateRun([
            CreateSnapshot("prime", ReconciliationSourceType.Prime, cash: [CreateCash("c1", 5000m, Thursday)]),
            CreateSnapshot("custodian", ReconciliationSourceType.Custodian, cash: [CreateCash("c2", 5000m, Thursday.AddHours(5))])
        ]);

        var result = engine.Run(run, profile);

        var match = result.matches.Should().ContainSingle().Subject;
        match.Classification.Should().Be(BreakClassification.Matched);
        match.RuleId.Should().Be("exact-cash-v1");
        match.ToleranceRuleId.Should().BeNull();
        var evidence = result.evidence.Single(e => e.EvidenceId == match.EvidenceIds.Single());
        evidence.Attributes["businessDayDelta"].Should().Be("0");
        evidence.Attributes["accountingPeriod"].Should().Be("2026-05-28");
    }

    [Fact]
    public void Run_CashEqualAmountsAcrossWeekend_ToleranceStageRecordsBusinessDayDelta()
    {
        var engine = new ReconciliationMatchingEngine();
        var profile = CashProfile(new CashToleranceRule("cash-abs-5-v1", 5m, null, TimeSpan.FromDays(1)));
        var run = CreateRun([
            CreateSnapshot("prime", ReconciliationSourceType.Prime, cash: [CreateCash("c1", 7500m, Friday)]),
            // Saturday posting resolves to the Monday accounting period, so the exact stage must
            // not claim it; the tolerance stage matches and records the one-business-day distance.
            CreateSnapshot("custodian", ReconciliationSourceType.Custodian, cash: [CreateCash("c2", 7500m, Friday.AddHours(5))])
        ]);

        var result = engine.Run(run, profile);

        var match = result.matches.Should().ContainSingle().Subject;
        match.Classification.Should().Be(BreakClassification.MatchedWithinTolerance);
        var evidence = result.evidence.Single(e => e.EvidenceId == match.EvidenceIds.Single());
        evidence.Attributes["businessDayDelta"].Should().Be("1");
        evidence.Attributes["settlementDateDeltaMinutes"].Should().Be("300");
    }

    [Fact]
    public void Run_CashSplit_OneToMany_RecordsSplitShapeInEvidence()
    {
        var engine = new ReconciliationMatchingEngine();
        var profile = CashProfile(new CashToleranceRule("cash-abs-001-v1", 0.01m, null, TimeSpan.FromDays(1)));
        var run = CreateRun([
            CreateSnapshot("prime", ReconciliationSourceType.Prime, cash: [CreateCash("c1", 1000m, Thursday)]),
            CreateSnapshot("custodian", ReconciliationSourceType.Custodian, cash:
            [
                CreateCash("c2", 400m, Thursday.AddHours(1)),
                CreateCash("c3", 600m, Thursday.AddHours(2))
            ])
        ]);

        var result = engine.Run(run, profile);

        result.breaks.Should().BeEmpty();
        var match = result.matches.Should().ContainSingle().Subject;
        match.Classification.Should().Be(BreakClassification.MatchedWithinTolerance);
        match.RuleId.Should().Be("cash-split-v1");
        match.ToleranceRuleId.Should().Be("cash-abs-001-v1");
        match.CashEntryIds.Should().BeEquivalentTo(["c1", "c2", "c3"]);
        var evidence = result.evidence.Single(e => e.EvidenceId == match.EvidenceIds.Single());
        evidence.Attributes["matchShape"].Should().Be("one-to-many");
        evidence.Attributes["anchorCashEntryId"].Should().Be("c1");
        evidence.Attributes["legCashEntryIds"].Should().Be("c2,c3");
        evidence.Attributes["amountResidualBase"].Should().Be("0");
    }

    [Fact]
    public void Run_CashSplit_ManyToOne_LabeledFromAnchorSide()
    {
        var engine = new ReconciliationMatchingEngine();
        var profile = CashProfile(new CashToleranceRule("cash-abs-001-v1", 0.01m, null, TimeSpan.FromDays(1)));
        var run = CreateRun([
            CreateSnapshot("prime", ReconciliationSourceType.Prime, cash:
            [
                CreateCash("c1", 400m, Thursday),
                CreateCash("c2", 600m, Thursday.AddHours(1))
            ]),
            CreateSnapshot("custodian", ReconciliationSourceType.Custodian, cash: [CreateCash("c3", 1000m, Thursday.AddHours(2))])
        ]);

        var result = engine.Run(run, profile);

        result.breaks.Should().BeEmpty();
        var match = result.matches.Should().ContainSingle().Subject;
        match.RuleId.Should().Be("cash-split-v1");
        var evidence = result.evidence.Single(e => e.EvidenceId == match.EvidenceIds.Single());
        evidence.Attributes["matchShape"].Should().Be("many-to-one");
        evidence.Attributes["anchorCashEntryId"].Should().Be("c3");
        evidence.Attributes["legCashEntryIds"].Should().Be("c1,c2");
    }

    [Fact]
    public void Run_CashWithDifferentBaseCurrencies_FailsClosedToBreaks()
    {
        var engine = new ReconciliationMatchingEngine();
        var profile = CashProfile(new CashToleranceRule("cash-abs-huge-v1", 1_000_000m, null, TimeSpan.FromDays(10)));
        var run = CreateRun([
            CreateSnapshot("prime", ReconciliationSourceType.Prime, cash: [CreateCash("c1", 1000m, Thursday, baseCurrency: "USD")]),
            // Fail-closed FX left this line in its source currency: identical numbers must not match.
            CreateSnapshot("custodian", ReconciliationSourceType.Custodian, cash: [CreateCash("c2", 1000m, Thursday, baseCurrency: "EUR")])
        ]);

        var result = engine.Run(run, profile);

        result.matches.Should().BeEmpty("amounts in different settlement currencies are incomparable");
        result.breaks.Should().HaveCount(2);
    }

    [Fact]
    public void Run_FuzzyReference_RequiresCrossSourcePair()
    {
        var engine = new ReconciliationMatchingEngine();
        var profile = CashProfile(new CashToleranceRule("cash-abs-001-v1", 0.01m, null, TimeSpan.FromDays(1)));
        var sameSourceRun = CreateRun([
            CreateSnapshot("prime", ReconciliationSourceType.Prime, cash:
            [
                CreateCash("c1", 100m, Thursday, counterpartyReference: "WIRE-77"),
                CreateCash("c2", 90_000m, Thursday, counterpartyReference: "WIRE-77")
            ])
        ]);

        var sameSource = engine.Run(sameSourceRun, profile);

        sameSource.matches.Should().BeEmpty("a shared reference inside one source is duplication, not reconciliation");
        sameSource.breaks.Should().HaveCount(2);

        var crossSourceRun = CreateRun([
            CreateSnapshot("prime", ReconciliationSourceType.Prime, cash: [CreateCash("c1", 100m, Thursday, settlementId: "SETL-9")]),
            CreateSnapshot("custodian", ReconciliationSourceType.Custodian, cash: [CreateCash("c2", 90_000m, Thursday, settlementId: "SETL-9")])
        ]);

        var crossSource = engine.Run(crossSourceRun, profile);

        var match = crossSource.matches.Should().ContainSingle().Subject;
        match.Classification.Should().Be(BreakClassification.PotentialBreak);
        match.RuleId.Should().Be("fuzzy-reference-v1");
        var evidence = crossSource.evidence.Single(e => e.EvidenceId == match.EvidenceIds.Single());
        evidence.Attributes["matchedOn"].Should().Be("settlementId");
    }

    [Fact]
    public void Run_PositionIdenticalTuplesAcrossSources_MatchExactGroup()
    {
        var engine = new ReconciliationMatchingEngine();
        var run = CreateRun([
            CreateSnapshot("prime", ReconciliationSourceType.Prime, positions: [CreatePosition("p1", "AAPL", 10m, 100m, 1000m)]),
            CreateSnapshot("custodian", ReconciliationSourceType.Custodian, positions: [CreatePosition("p2", "AAPL", 10m, 100m, 1000m)])
        ]);

        var result = engine.Run(run, DefaultProfile());

        result.breaks.Should().BeEmpty();
        var match = result.matches.Should().ContainSingle().Subject;
        match.Classification.Should().Be(BreakClassification.Matched);
        match.RuleId.Should().Be("exact-position-v1");
        match.PositionIds.Should().BeEquivalentTo(["p1", "p2"]);
    }

    [Fact]
    public void Run_PositionExactDuplicatesWithinSingleSource_DoNotSelfMatch()
    {
        var engine = new ReconciliationMatchingEngine();
        var run = CreateRun([
            CreateSnapshot("prime", ReconciliationSourceType.Prime, positions:
            [
                CreatePosition("p1", "AAPL", 10m, 100m, 1000m),
                CreatePosition("p2", "AAPL", 10m, 100m, 1000m)
            ])
        ]);

        var result = engine.Run(run, DefaultProfile());

        result.matches.Should().BeEmpty("identical tuples inside one feed are duplicated data, not a cross-source match");
        var breakRecord = result.breaks.Should().ContainSingle(b => b.Classification == BreakClassification.TrueBreak).Subject;
        var evidence = result.evidence.Single(e => e.EvidenceId == breakRecord.EvidenceIds.Single());
        evidence.Attributes["unresolvedPositionIds"].Should().Be("p1,p2");
    }

    [Fact]
    public void Run_PositionSplit_AggregatesLotsAcrossSources()
    {
        var engine = new ReconciliationMatchingEngine();
        var profile = new StatementToleranceProfile(
            "lot-aggregation",
            1,
            [],
            [new PositionToleranceRule("pos-lots-v1", 0.5m, 10m, 0.5m)],
            []);
        var run = CreateRun([
            CreateSnapshot("prime", ReconciliationSourceType.Prime, positions: [CreatePosition("p1", "AAPL", 100m, 50m, 5000m)]),
            CreateSnapshot("custodian", ReconciliationSourceType.Custodian, positions:
            [
                CreatePosition("p2", "AAPL", 60m, 50m, 3000m),
                CreatePosition("p3", "AAPL", 40m, 50m, 2000m)
            ])
        ]);

        var result = engine.Run(run, profile);

        result.breaks.Should().BeEmpty();
        var match = result.matches.Should().ContainSingle().Subject;
        match.RuleId.Should().Be("position-split-v1");
        match.ToleranceRuleId.Should().Be("pos-lots-v1");
        match.PositionIds.Should().BeEquivalentTo(["p1", "p2", "p3"]);
        var evidence = result.evidence.Single(e => e.EvidenceId == match.EvidenceIds.Single());
        evidence.Attributes["matchShape"].Should().Be("one-to-many");
        evidence.Attributes["anchorPositionId"].Should().Be("p1");
        evidence.Attributes["legPositionIds"].Should().Be("p2,p3");
    }

    [Fact]
    public void Run_PositionsWithDifferentCurrencies_FailClosed()
    {
        var engine = new ReconciliationMatchingEngine();
        var run = CreateRun([
            CreateSnapshot("prime", ReconciliationSourceType.Prime, positions: [CreatePosition("p1", "SAP", 10m, 100m, 1000m, currency: "USD")]),
            CreateSnapshot("custodian", ReconciliationSourceType.Custodian, positions: [CreatePosition("p2", "SAP", 10m, 100m, 1000m, currency: "EUR")])
        ]);

        var result = engine.Run(run, DefaultProfile());

        result.matches.Should().BeEmpty("identical numbers in different currencies are incomparable");
        result.breaks.Should().ContainSingle(b => b.Classification == BreakClassification.TrueBreak);
    }

    [Fact]
    public void Run_ReEvaluatingSameRun_ProducesIdenticalArtifactIds()
    {
        var engine = new ReconciliationMatchingEngine();
        var profile = CashProfile(new CashToleranceRule("cash-abs-5-v1", 5m, null, TimeSpan.FromDays(1)));
        var run = CreateRun([
            CreateSnapshot("prime", ReconciliationSourceType.Prime, cash:
            [
                CreateCash("c1", 1000m, Thursday),
                CreateCash("c4", 77m, Thursday)
            ]),
            CreateSnapshot("custodian", ReconciliationSourceType.Custodian, cash:
            [
                CreateCash("c2", 400m, Thursday.AddHours(1)),
                CreateCash("c3", 600m, Thursday.AddHours(2))
            ])
        ]);

        var first = engine.Run(run, profile);
        var second = engine.Run(run, profile);

        second.matches.Select(static m => m.MatchGroupId).Should().Equal(first.matches.Select(static m => m.MatchGroupId));
        second.breaks.Select(static b => b.BreakId).Should().Equal(first.breaks.Select(static b => b.BreakId));
        second.evidence.Select(static e => e.EvidenceId).Should().Equal(first.evidence.Select(static e => e.EvidenceId));
        first.matches.Should().NotBeEmpty();
        first.breaks.Should().NotBeEmpty();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static StatementToleranceProfile DefaultProfile() => new(
        "floor-tests",
        1,
        [new CashToleranceRule("cash-default-v1", 0.01m, null, TimeSpan.FromMinutes(5))],
        [new PositionToleranceRule("position-default-v1", 0m, 0m, 0m)],
        []);

    private static StatementToleranceProfile CashProfile(params CashToleranceRule[] rules) => new(
        "floor-tests-cash",
        1,
        rules,
        [],
        []);

    private static ReconciliationRun CreateRun(IReadOnlyList<DataSourceSnapshot> snapshots) =>
        new(Guid.NewGuid(), new DateOnly(2026, 5, 28), Thursday, false, 1, "recon:2026-05-28", snapshots);

    private static DataSourceSnapshot CreateSnapshot(
        string id,
        ReconciliationSourceType sourceType,
        IReadOnlyList<NormalizedPosition>? positions = null,
        IReadOnlyList<NormalizedCashEntry>? cash = null) =>
        new(id, sourceType, Thursday, "v1", positions ?? [], cash ?? []);

    private static NormalizedPosition CreatePosition(
        string id,
        string canonicalId,
        decimal quantity,
        decimal price,
        decimal marketValue,
        string currency = "USD") =>
        new(id, canonicalId, null, null, canonicalId, null, quantity, price, marketValue, currency, Thursday, id);

    private static NormalizedCashEntry CreateCash(
        string id,
        decimal amountBase,
        DateTimeOffset postedAtUtc,
        string baseCurrency = "USD",
        string? counterpartyReference = null,
        string? settlementId = null) =>
        new(id, "acct", amountBase, baseCurrency, amountBase, baseCurrency, postedAtUtc, new DateOnly(2026, 5, 28), id, counterpartyReference, settlementId);
}
