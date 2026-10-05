using System.Collections.Immutable;
using System.Globalization;
using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Reporting;

namespace Meridian.Tests.Reporting;

/// <summary>
/// Guards month-end income restatements against invented journal explanations, hidden residuals,
/// incompatible accounting scopes, and replay drift after the underlying source changes.
/// </summary>
public sealed class InvestmentIncomeComparisonEngineTests
{
    private static readonly DateOnly PeriodEnd = new(2026, 5, 31);
    private static readonly DateTimeOffset RetainedAt = new(2026, 6, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid LedgerBookId = Guid.Parse("952d9550-c652-4943-95e2-452191c775ec");
    private const string GridId = "investment-income";
    private const string Metric = "income";

    [Fact]
    public void Compare_LateAccrualAfterPublication_ExplainsMovementThroughExactRetainedJournal()
    {
        var published = Manifest("may-original", [IncomeLine("coupon-may", "coupon-line", -1_000m)]);
        var restated = Manifest("may-restated", [
            IncomeLine("coupon-may", "coupon-line", -1_000m),
            IncomeLine("late-accrual-20260531", "late-accrual-income-line", -125m)
        ], revision: 2, priorRunId: published.RunId);

        var retained = Compare(published, restated);

        retained.Comparison.Movement.Should().Be(-125m);
        retained.Comparison.ExplainedAmount.Should().Be(-125m);
        retained.Comparison.ResidualAmount.Should().Be(0m);
        retained.Comparison.Compatible.Should().BeTrue();
        retained.Comparison.Status.Should().Be("Reconciled");
        var contribution = retained.Comparison.Contributions.Should().ContainSingle(c => c.Kind == "Journal").Subject;
        contribution.Amount.Should().Be(-125m);
        contribution.RecordId.Should().Be("late-accrual-20260531");
        contribution.SourceRunId.Should().Be(restated.RunId);
        var support = retained.Support.Single(s => s.Contribution.ContributionId == contribution.ContributionId);
        support.ComparisonId.Should().Be(retained.Comparison.ComparisonId);
        support.BaselineRunId.Should().Be(published.RunId);
        support.CurrentRunId.Should().Be(restated.RunId);
        support.BaselineRecords.Should().BeEmpty();
        var row = support.CurrentRecords.Should().ContainSingle().Subject;
        row["entryId"].Should().Be("late-accrual-income-line");
        row["journalEntryId"].Should().Be("late-accrual-20260531");
        row["netAmount"].Should().Be("-125");
        row["timestampUtc"].Should().Be("2026-05-31T23:59:00.0000000+00:00");
        AssertBridgeBalances(retained);
    }

    [Fact]
    public void Compare_OriginalOrRestatedBaseline_UsesExactlyTheSelectedRetainedRun()
    {
        var original = Manifest("may-original", [IncomeLine("coupon", "coupon-line", -1_000m)]);
        var restated = Manifest("may-restated", [
            IncomeLine("coupon", "coupon-line", -1_000m),
            IncomeLine("first-accrual", "first-accrual-line", -125m)
        ], revision: 2, priorRunId: original.RunId);
        var latest = Manifest("may-restated-again", [
            IncomeLine("coupon", "coupon-line", -1_000m),
            IncomeLine("first-accrual", "first-accrual-line", -125m),
            IncomeLine("second-accrual", "second-accrual-line", -15m)
        ], revision: 3, priorRunId: restated.RunId);

        var fromOriginal = Compare(original, latest);
        var fromRestated = Compare(restated, latest);

        fromOriginal.Comparison.Baseline.RunId.Should().Be(original.RunId);
        fromOriginal.Comparison.Movement.Should().Be(-140m);
        fromRestated.Comparison.Baseline.RunId.Should().Be(restated.RunId);
        fromRestated.Comparison.Movement.Should().Be(-15m);
        fromOriginal.Comparison.Baseline.RunId.Should().NotBe(fromRestated.Comparison.Baseline.RunId);
        fromOriginal.BaselineManifest.RunAttemptOrdinal.Should().Be(1);
        fromRestated.BaselineManifest.RunAttemptOrdinal.Should().Be(2);
        fromRestated.BaselineManifest.PriorRunId.Should().Be(original.RunId);
    }

    [Fact]
    public void Compare_UnsupportedOffsettingCorrections_RemainUnexplainedWhenNetMovementIsZero()
    {
        var originalA = IncomeLine("", "unsupported-a", -100m, instrumentId: "bond-a");
        var originalB = IncomeLine("", "unsupported-b", -100m, instrumentId: "bond-b");
        var currentA = IncomeLine("", "unsupported-a", -120m, instrumentId: "bond-a");
        var currentB = IncomeLine("", "unsupported-b", -80m, instrumentId: "bond-b");

        var retained = Compare(
            Manifest("original", [originalA, originalB]),
            Manifest("restated", [currentA, currentB], revision: 2));

        retained.Comparison.Movement.Should().Be(0m);
        retained.Comparison.ResidualAmount.Should().Be(0m);
        retained.Comparison.Status.Should().Be("Unexplained");
        retained.Comparison.Warnings.Should().NotBeEmpty();
        retained.Comparison.Contributions.Should().NotContain(c => c.Kind == "Journal");
        var unsupported = retained.Comparison.Contributions.Where(c => c.Kind == "Unexplained").ToArray();
        unsupported.Select(c => c.Amount).Should().BeEquivalentTo(new[] { -20m, 20m });
        foreach (var contribution in unsupported)
        {
            var support = retained.Support.Single(s => s.Contribution.ContributionId == contribution.ContributionId);
            support.BaselineRecords.Should().ContainSingle();
            support.CurrentRecords.Should().ContainSingle();
        }
        retained.Comparison.GridDiff.ChangedRowCount.Should().Be(2);
        retained.BaselineManifest.CertifiedDatasetRows.Should().HaveCount(2);
        retained.CurrentManifest.CertifiedDatasetRows.Should().HaveCount(2);
        AssertBridgeBalances(retained);
    }

    [Fact]
    public void Compare_ExpandedEntityPopulation_DoesNotCountNewEntityIncomeAgainAsJournals()
    {
        var baselineParameters = Parameters() with
        {
            Scope = new ReportingRunScopeDto("fund-alpha", ReportingEntityScopeKindDto.Entity, EntityId: "entity-a")
        };
        var currentParameters = Parameters() with
        {
            Scope = new ReportingRunScopeDto("fund-alpha", ReportingEntityScopeKindDto.AllEntities)
        };
        var baseline = Manifest("entity-a-original", [IncomeLine("coupon-a", "coupon-a-line", -100m)], baselineParameters);
        var current = Manifest("all-entities-restated", [
            IncomeLine("coupon-a", "coupon-a-line", -100m),
            IncomeLine("late-accrual-a", "accrual-a-line", -25m),
            IncomeLine("coupon-b", "coupon-b-line", -80m, entityId: "entity-b")
        ], currentParameters, revision: 2);

        var retained = Compare(baseline, current);

        retained.Comparison.Movement.Should().Be(-105m);
        retained.Comparison.Contributions.Where(c => c.Kind == "Population").Sum(c => c.Amount).Should().Be(-80m);
        retained.Comparison.Contributions.Where(c => c.Kind == "Journal").Sum(c => c.Amount).Should().Be(-25m);
        retained.Comparison.Contributions.Should().NotContain(c => c.Kind == "Journal" && c.RecordId == "coupon-b");
        retained.Comparison.Differences.Should().Contain(d => d.Dimension == "Population" && d.Baseline != d.Current);
        retained.Comparison.ResidualAmount.Should().Be(0m);
        AssertBridgeBalances(retained);
    }

    [Fact]
    public void Compare_FirstAccrualForAnInstrumentInUnchangedScope_DoesNotInferAnInvestmentPopulationChange()
    {
        var baseline = Manifest("original", [IncomeLine("coupon-a", "coupon-a-line", -100m, instrumentId: "bond-a")]);
        var current = Manifest("restated", [
            IncomeLine("coupon-a", "coupon-a-line", -100m, instrumentId: "bond-a"),
            IncomeLine("first-accrual-b", "first-accrual-b-line", -25m, instrumentId: "bond-b")
        ]);

        var retained = Compare(baseline, current);

        retained.Comparison.Contributions.Should().NotContain(c => c.Kind == "Population",
            "a new income journal for a security does not prove that the investment entered the report population");
        var contribution = retained.Comparison.Contributions.Should().ContainSingle(c => c.Kind == "Journal").Subject;
        contribution.RecordId.Should().Be("first-accrual-b");
        contribution.Amount.Should().Be(-25m);
        retained.Comparison.ResidualAmount.Should().Be(0m);
        AssertBridgeBalances(retained);
    }

    [Theory]
    [InlineData("Period")]
    [InlineData("Accounting basis")]
    [InlineData("Currency")]
    [InlineData("Ledger book")]
    public void Compare_IncompatibleInputsWithIdenticalTotals_CannotAppearReconciled(string dimension)
    {
        var originalParameters = Parameters();
        var currentParameters = dimension switch
        {
            "Period" => originalParameters with { PeriodId = "period-2026-06" },
            "Accounting basis" => originalParameters with { AccountingBasis = ReportingAccountingBasisDto.Tax },
            "Currency" => originalParameters with { PresentationCurrency = "EUR" },
            "Ledger book" => originalParameters with { LedgerBook = new ReportingLedgerBookSelectionDto(Guid.Parse("0e2b74d3-9770-47fb-9b6d-21c5938f7d8f"), "TAX") },
            _ => throw new ArgumentOutOfRangeException(nameof(dimension))
        };
        var original = Manifest("original", [IncomeLine("coupon", "coupon-line", -100m)], originalParameters);
        var current = Manifest("current", [IncomeLine("coupon", "coupon-line", -100m)], currentParameters);

        var retained = Compare(original, current);

        retained.Comparison.Compatible.Should().BeFalse();
        retained.Comparison.Status.Should().Be("Incompatible");
        retained.Comparison.Differences.Should().Contain(d => !d.Compatible);
        retained.Comparison.Status.Should().NotBe("Reconciled");
    }

    [Theory]
    [InlineData("Period")]
    [InlineData("Currency")]
    [InlineData("Ledger book")]
    [InlineData("Accounting basis")]
    public void Compare_IdenticalInvalidAccountingMetadata_CannotReconcileByEquality(string dimension)
    {
        var parameters = Parameters();
        parameters = dimension switch
        {
            "Period" => parameters with { PeriodId = " " },
            "Currency" => parameters with { PresentationCurrency = " " },
            "Ledger book" => parameters with { LedgerBook = new ReportingLedgerBookSelectionDto(Guid.Empty, "") },
            "Accounting basis" => parameters with { AccountingBasis = (ReportingAccountingBasisDto)999 },
            _ => throw new ArgumentOutOfRangeException(nameof(dimension))
        };
        var income = IncomeLine("coupon", "coupon-line", -100m);
        if (dimension == "Currency")
            income["currency"] = " ";

        var retained = Compare(
            Manifest("original", [income], parameters),
            Manifest("restated", [income], parameters));

        retained.Comparison.Movement.Should().Be(0m);
        retained.Comparison.Compatible.Should().BeFalse();
        retained.Comparison.Status.Should().Be("Incompatible");
        retained.Comparison.Differences.Should().Contain(d => d.Dimension == "Input completeness" && !d.Compatible);
    }

    [Fact]
    public void Compare_AccrualReversal_ExplainsSignedMovementWithTheReversalJournal()
    {
        var original = Manifest("original", [IncomeLine("accrual", "accrual-income-line", -125m)]);
        var reversed = Manifest("reversed", [
            IncomeLine("accrual", "accrual-income-line", -125m),
            IncomeLine("accrual-reversal", "reversal-income-line", 125m)
        ]);

        var retained = Compare(original, reversed);

        retained.Comparison.Movement.Should().Be(125m);
        var contribution = retained.Comparison.Contributions.Should().ContainSingle(c => c.Kind == "Journal").Subject;
        contribution.RecordId.Should().Be("accrual-reversal");
        contribution.Amount.Should().Be(125m);
        retained.Comparison.ResidualAmount.Should().Be(0m);
        AssertBridgeBalances(retained);
    }

    [Fact]
    public void Compare_BalancedLateAccrualJournal_AttributesOnlyRetainedIncomeLinesPassingTheReportFilter()
    {
        var couponReceivable = IncomeLine("coupon", "coupon-receivable-line", 1_000m);
        couponReceivable["accountType"] = "Asset";
        couponReceivable["account"] = "Interest receivable";
        var accrualReceivable = IncomeLine("late-accrual", "accrual-receivable-line", 125m);
        accrualReceivable["accountType"] = "Asset";
        accrualReceivable["account"] = "Interest receivable";
        var filter = new ReportWriterFilterDefinitionDto("accountType", ReportWriterFilterOperatorDto.Equals, "Revenue");
        var original = Manifest("original", [
            IncomeLine("coupon", "coupon-income-line", -1_000m), couponReceivable
        ], filters: [filter]);
        var current = Manifest("restated", [
            IncomeLine("coupon", "coupon-income-line", -1_000m), couponReceivable,
            IncomeLine("late-accrual", "accrual-income-line", -125m), accrualReceivable
        ], filters: [filter]);

        var retained = Compare(original, current);

        retained.Comparison.Movement.Should().Be(-125m);
        retained.Comparison.ExplainedAmount.Should().Be(-125m);
        retained.Comparison.ResidualAmount.Should().Be(0m);
        var contribution = retained.Comparison.Contributions.Should().ContainSingle(c => c.Kind == "Journal").Subject;
        contribution.RecordId.Should().Be("late-accrual");
        contribution.Amount.Should().Be(-125m);
        var support = retained.Support.Single(s => s.Contribution.ContributionId == contribution.ContributionId);
        support.CurrentRecords.Should().Contain(r => r["entryId"] == "accrual-income-line");
        AssertBridgeBalances(retained);
    }

    [Fact]
    public void Compare_LateAccrualAndUnsupportedReportAdjustment_PreservesTheUnsupportedResidual()
    {
        var original = Manifest("original", [IncomeLine("coupon", "coupon-line", -1_000m)]);
        var supported = Manifest("restated", [
            IncomeLine("coupon", "coupon-line", -1_000m),
            IncomeLine("late-accrual", "late-accrual-line", -125m)
        ]);
        var retainedGrid = supported.RenderedReportWriterGrids.Single();
        var incomeRow = retainedGrid.Rows.Single();
        var adjustedValues = incomeRow.Values.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        adjustedValues[Metric] = "-1140";
        var adjusted = supported with
        {
            RenderedReportWriterGrids = [retainedGrid with { Rows = [incomeRow with { Values = adjustedValues }] }]
        };

        var retained = Compare(original, adjusted);

        retained.Comparison.Movement.Should().Be(-140m);
        retained.Comparison.ResidualAmount.Should().NotBe(0m);
        retained.Comparison.Status.Should().Be("Unexplained");
        retained.Comparison.Warnings.Should().NotBeEmpty();
        AssertBridgeBalances(retained);
    }

    [Fact]
    public void Compare_OffsettingRenderedAdjustmentsWithoutSourceChanges_CannotHideBehindUnchangedTotals()
    {
        IReadOnlyList<IReadOnlyDictionary<string, string>> rows = [
            IncomeLine("coupon-a", "coupon-a-line", -100m, instrumentId: "bond-a"),
            IncomeLine("coupon-b", "coupon-b-line", -100m, instrumentId: "bond-b")
        ];
        var original = Manifest("original", rows);
        var current = Manifest("restated", rows);
        var grid = current.RenderedReportWriterGrids.Single();
        var alteredRows = grid.Rows.Select(row =>
        {
            var values = row.Values.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
            values[Metric] = values["instrumentId"] == "bond-a" ? "-120" : "-80";
            return row with { Values = values };
        }).ToArray();
        current = current with { RenderedReportWriterGrids = [grid with { Rows = alteredRows }] };

        var retained = Compare(original, current);

        retained.Comparison.BaselineAmount.Should().Be(-200m);
        retained.Comparison.CurrentAmount.Should().Be(-200m);
        retained.Comparison.Movement.Should().Be(0m);
        retained.Comparison.ResidualAmount.Should().Be(0m);
        retained.Comparison.Status.Should().Be("Unexplained");
        retained.Comparison.GridDiff.ChangedRowCount.Should().Be(2);
        retained.Comparison.Warnings.Should().NotBeEmpty();
        retained.Comparison.Contributions.Should().NotContain(c =>
            c.Kind == "Journal" || c.Kind == "Methodology" || c.Kind == "Population");
        var unsupported = retained.Comparison.Contributions.Where(c => c.Kind == "Unexplained").ToArray();
        unsupported.Should().NotBeEmpty();
        foreach (var contribution in unsupported)
        {
            var support = retained.Support.Single(s => s.Contribution.ContributionId == contribution.ContributionId);
            support.BaselineRecords.Concat(support.CurrentRecords).Should().NotBeEmpty();
        }
        AssertBridgeBalances(retained);
    }

    [Fact]
    public void Compare_DocumentedAccrualMethodologyChange_UsesItsQuantifiedRetainedEvidence()
    {
        var income = IncomeLine("coupon", "coupon-income-line", -100m);
        income["accrualAdjustedIncome"] = "-120";
        var original = Manifest("original", [income]);
        var current = Manifest("restated", [income], sourceField: "accrualAdjustedIncome") with
        {
            IncomeMethodologyEvidence = [new ReportingIncomeMethodologyEvidence(
                "accrual-policy-may-2026", original.RunId, GridId, Metric,
                "Apply documented effective-interest accrual policy", -100m, -120m,
                ["coupon-income-line"], ["evidence-vault:effective-interest-policy-v2"])]
        };

        var retained = Compare(original, current);

        retained.Comparison.Status.Should().Be("Reconciled");
        retained.Comparison.Movement.Should().Be(-20m);
        retained.Comparison.ResidualAmount.Should().Be(0m);
        retained.Comparison.Contributions.Should().NotContain(c => c.Kind == "Journal");
        var contribution = retained.Comparison.Contributions.Should().ContainSingle(c => c.Kind == "Methodology").Subject;
        contribution.RecordId.Should().Be("accrual-policy-may-2026");
        contribution.Amount.Should().Be(-20m);
        var support = retained.Support.Single(s => s.Contribution.ContributionId == contribution.ContributionId);
        support.EvidenceReferences.Should().Contain("evidence-vault:effective-interest-policy-v2");
        support.BaselineRecords.Should().Contain(r => r["entryId"] == "coupon-income-line");
        support.CurrentRecords.Should().Contain(r => r["entryId"] == "coupon-income-line");
        AssertBridgeBalances(retained);
    }

    [Fact]
    public void Compare_RetainedFilterPolicyChanges_ExplainsUnchangedJournalLinesFromBothDefinitions()
    {
        var interest = IncomeLine("coupon", "coupon-income-line", -100m);
        var otherIncome = IncomeLine("other-income", "other-income-line", -20m);
        otherIncome["accountType"] = "OtherIncome";
        var original = Manifest("original", [interest, otherIncome], filters: [
            new ReportWriterFilterDefinitionDto("accountType", ReportWriterFilterOperatorDto.Equals, "Revenue")
        ]);
        var current = Manifest("restated", [interest, otherIncome], filters: [
            new ReportWriterFilterDefinitionDto("accountType", ReportWriterFilterOperatorDto.NotEquals, "Asset")
        ]);

        var retained = Compare(original, current);

        retained.Comparison.Status.Should().Be("Reconciled");
        retained.Comparison.Movement.Should().Be(-20m);
        retained.Comparison.ResidualAmount.Should().Be(0m);
        var contribution = retained.Comparison.Contributions.Should().ContainSingle(c => c.Kind == "Methodology").Subject;
        contribution.Amount.Should().Be(-20m);
        var support = retained.Support.Single(s => s.Contribution.ContributionId == contribution.ContributionId);
        support.BaselineRecords.Should().NotBeEmpty();
        support.CurrentRecords.Should().NotBeEmpty();
        support.EvidenceReferences.Should().NotBeEmpty();
        retained.BaselineManifest.RenderedReportWriterGrids.Single().Lineage!.Filters.Should().ContainSingle(f => f.Value == "Revenue");
        retained.CurrentManifest.RenderedReportWriterGrids.Single().Lineage!.Filters.Should().ContainSingle(f => f.Value == "Asset");
        AssertBridgeBalances(retained);
    }

    [Fact]
    public void Compare_MethodologyAndNewJournalChangeTogether_LeavesTheirUnsupportedCombinedEffectVisible()
    {
        var coupon = IncomeLine("coupon", "coupon-income-line", -100m);
        coupon["accrualAdjustedIncome"] = "-120";
        var lateAccrual = IncomeLine("late-accrual", "late-accrual-line", -10m);
        lateAccrual["accrualAdjustedIncome"] = "-15";
        var original = Manifest("original", [coupon]);
        var current = Manifest("restated", [coupon, lateAccrual], sourceField: "accrualAdjustedIncome");

        var retained = Compare(original, current);

        retained.Comparison.Status.Should().Be("Unexplained");
        retained.Comparison.Movement.Should().Be(-35m);
        retained.Comparison.ExplainedAmount.Should().Be(-20m);
        retained.Comparison.ResidualAmount.Should().Be(-15m);
        retained.Comparison.Contributions.Should().Contain(c => c.Kind == "Methodology" && c.Amount == -20m);
        retained.Comparison.Contributions.Should().Contain(c => c.Kind == "Unexplained" && c.Amount == -15m);
        retained.Comparison.Contributions.Should().NotContain(c => c.Kind == "Journal");
        AssertBridgeBalances(retained);
    }

    [Theory]
    [InlineData(false, -120)]
    [InlineData(true, -130)]
    public void Compare_MethodologyClaimWithoutMatchingEvidence_RemainsAnUnexplainedResidual(bool hasDocumentation, int claimedCurrentAmount)
    {
        var income = IncomeLine("coupon", "coupon-income-line", -100m);
        income["accrualAdjustedIncome"] = "-120";
        var original = Manifest("original", [income]);
        var current = Manifest("restated", [income], sourceField: "accrualAdjustedIncome") with
        {
            IncomeMethodologyEvidence = [new ReportingIncomeMethodologyEvidence(
                "unproven-policy", original.RunId, GridId, Metric,
                "Claimed accrual policy adjustment", -100m, claimedCurrentAmount,
                ["coupon-income-line"], hasDocumentation ? ["evidence-vault:policy-v2"] : [])]
        };

        var retained = Compare(original, current);

        retained.Comparison.Status.Should().Be("Unexplained");
        retained.Comparison.ResidualAmount.Should().Be(-20m);
        retained.Comparison.Contributions.Should().NotContain(c => c.Kind == "Methodology");
        retained.Comparison.Warnings.Should().NotBeEmpty();
        AssertBridgeBalances(retained);
    }

    [Fact]
    public void Compare_ExistingJournalLineRewritten_CannotPassAsANewRetainedJournal()
    {
        var original = Manifest("original", [IncomeLine("coupon", "coupon-income-line", -100m)]) with
        {
            ResolvedTemplate = new VersionedReportTemplateIdDto("investment-income", 1)
        };
        var current = Manifest("restated", [IncomeLine("coupon", "coupon-income-line", -120m)]) with
        {
            ResolvedTemplate = new VersionedReportTemplateIdDto("investment-income", 2)
        };

        var retained = Compare(original, current);

        retained.Comparison.Movement.Should().Be(-20m);
        retained.Comparison.ResidualAmount.Should().Be(-20m);
        retained.Comparison.Status.Should().Be("Unexplained");
        retained.Comparison.Contributions.Should().OnlyContain(c => c.Kind == "Unexplained");
        AssertBridgeBalances(retained);
    }

    [Fact]
    public void Compare_SourceRowsChangeAfterRetention_ReplaysFrozenInputsAndExactSupportingRecord()
    {
        var lateAccrual = IncomeLine("late-accrual", "late-accrual-line", -125m);
        var original = Manifest("original", [IncomeLine("coupon", "coupon-line", -1_000m)]);
        var current = Manifest("restated", [IncomeLine("coupon", "coupon-line", -1_000m), lateAccrual], revision: 2);
        var retained = Compare(original, current);
        var comparisonId = retained.Comparison.ComparisonId;

        lateAccrual["netAmount"] = "-9999";
        lateAccrual["journalEntryId"] = "live-replacement";
        lateAccrual["description"] = "Live source was overwritten after the report was retained";
        var replayed = Compare(retained.BaselineManifest, retained.CurrentManifest);

        retained.CurrentManifest.CertifiedDatasetRows.Single(r => r["entryId"] == "late-accrual-line")["netAmount"].Should().Be("-125");
        retained.Support.SelectMany(s => s.CurrentRecords).Should().Contain(r =>
            r["entryId"] == "late-accrual-line" && r["journalEntryId"] == "late-accrual" && r["netAmount"] == "-125");
        replayed.Comparison.ComparisonId.Should().Be(comparisonId);
        replayed.Comparison.Should().BeEquivalentTo(retained.Comparison);
        replayed.Support.Should().BeEquivalentTo(retained.Support);
    }

    private static RetainedReportingIncomeComparison Compare(ReportingOutputManifest baseline, ReportingOutputManifest current) =>
        ReportingIncomeComparisonEngine.Compare(baseline, current, GridId, Metric, retainedAtUtc: RetainedAt);

    private static void AssertBridgeBalances(RetainedReportingIncomeComparison retained)
    {
        var comparison = retained.Comparison;
        comparison.ExplainedAmount.Should().Be(comparison.Contributions
            .Where(c => c.Kind is "Journal" or "Population" or "Methodology").Sum(c => c.Amount));
        (comparison.ExplainedAmount + comparison.ResidualAmount).Should().Be(comparison.Movement);
    }

    private static ReportingRunParametersDto Parameters() => new(
        new ReportingRunScopeDto("fund-alpha"),
        "period-2026-05",
        PeriodEnd,
        new ReportingLedgerBookSelectionDto(LedgerBookId, "PRIMARY"),
        ReportingAccountingBasisDto.Gaap,
        "USD",
        ReportingConsolidationLevelDto.Fund,
        ReportingOutputFormatDto.Pdf,
        ReportingFinalityDto.Final,
        IncludeSupportingSchedules: true,
        IncludeEvidenceAppendix: true);

    private static ReportingOutputManifest Manifest(
        string runId,
        IReadOnlyList<IReadOnlyDictionary<string, string>> rows,
        ReportingRunParametersDto? parameters = null,
        int revision = 1,
        string? priorRunId = null,
        string sourceField = "netAmount",
        IReadOnlyList<ReportWriterFilterDefinitionDto>? filters = null)
    {
        var grids = ReportWriterGridEngine.RenderGrids([
            new ReportWriterGridDefinitionDto(
                GridId,
                "Investment income",
                ReportWriterGridKindDto.Pivot,
                RowFields: ["entityId", "instrumentId"],
                Metrics: [new ReportWriterMetricDefinitionDto(Metric, sourceField)],
                Filters: filters)
        ], rows);
        return new ReportingOutputManifest(
            runId,
            "investment-income",
            PeriodEnd,
            ReportingRunStatus.Released,
            Sections: [],
            Artifacts: [],
            AttemptCount: 1,
            ReportingRunTrigger.AdHoc,
            RenderedReportWriterGrids: grids.ToImmutableArray(),
            RunSeriesId: "investment-income-20260531",
            RunAttemptOrdinal: revision,
            PriorRunId: priorRunId,
            ResolvedParameters: parameters ?? Parameters(),
            CertifiedDatasetRows: rows.ToImmutableArray());
    }

    private static Dictionary<string, string> IncomeLine(
        string journalId,
        string entryId,
        decimal netAmount,
        string entityId = "entity-a",
        string instrumentId = "bond-us-treasury") => new(StringComparer.Ordinal)
        {
            ["journalEntryId"] = journalId,
            ["entryId"] = entryId,
            ["fundId"] = "fund-alpha",
            ["entityId"] = entityId,
            ["instrumentId"] = instrumentId,
            ["account"] = "Investment interest income",
            ["accountType"] = "Revenue",
            ["netAmount"] = netAmount.ToString("G29", CultureInfo.InvariantCulture),
            ["debit"] = Math.Max(netAmount, 0m).ToString("G29", CultureInfo.InvariantCulture),
            ["credit"] = Math.Max(-netAmount, 0m).ToString("G29", CultureInfo.InvariantCulture),
            ["currency"] = "USD",
            ["accountingBasis"] = "Gaap",
            ["periodId"] = "period-2026-05",
            ["timestampUtc"] = "2026-05-31T23:59:00.0000000+00:00",
            ["recordedAtUtc"] = "2026-06-03T09:00:00.0000000+00:00",
            ["description"] = "Retained investment income journal",
            ["globalSequence"] = "42"
        };
}
