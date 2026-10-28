using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Ledger;
using Meridian.Ledger;
using Meridian.Tests.Ledger;

namespace Meridian.Tests.FinancialOperations.Ledger;

public sealed partial class AccountingPostingCandidateServiceTests
{
    [Theory]
    [InlineData(110)]
    [InlineData(90)]
    public async Task CanonicalDisposalCandidate_ReportsActualInvestingProceeds_AndReverses(int basis)
    {
        var result = await BuildCashFlowDisposalCandidateAsync(basis);
        result.Candidate.HasBlockingIssues.Should().BeFalse(string.Join("; ", result.Candidate.Issues.Select(issue => issue.Message)));
        var entry = result.Write!.Entry;
        var ledger = new Meridian.Ledger.Ledger();
        ledger.Post(entry);

        CanonicalCashFlowTestData.AssertReportAndReversal(ledger, entry, 100m);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposalCandidate_CompoundOrWrongSelectedBasis_CannotAcquireCashCategory(bool wrongBasis)
    {
        var result = await BuildCashFlowDisposalCandidateAsync(110, compound: !wrongBasis,
            selectedBasis: wrongBasis ? 109m : null);
        result.Candidate.HasBlockingIssues.Should().BeFalse();
        var entry = result.Write!.Entry;
        entry.Metadata.Tags!.Keys.Should().NotContain(key => key.StartsWith("cash-flow.category."));
        var ledger = new Meridian.Ledger.Ledger();
        ledger.Post(entry);

        var report = () => LedgerReportPackBuilder.Build(ledger, new LedgerReportPackRequest("refusal", "fund", "period",
            CanonicalCashFlowTestData.Start, CanonicalCashFlowTestData.Start.AddYears(1),
            CanonicalCashFlowTestData.Start.AddYears(1), "USD", "reviewer", CanonicalCashFlowTestData.Start.AddYears(1)));

        report.Should().Throw<LedgerValidationException>().WithMessage("*cash-flow attribution*");
    }

    [Theory]
    [InlineData(101.5)]
    [InlineData(98.5)]
    public async Task FactorPaydownCandidate_ReportsActualPrincipalWithoutCashGainOrLoss_AndReverses(double price)
    {
        var harness = await CreateAuthoritativeFactorHarnessAsync(
            [BuildFaceLotOfRecord(AuthoritativeFactorHarness.LedgerBookId, AuthoritativeFactorHarness.SecurityId,
                AuthoritativeFactorHarness.PositionId, 950_000m, .95m, 100m,
                pricePercentOfPar: (decimal)price)], .95m, .90m);
        var result = await harness.BuildWriteAsync();
        result.Candidate.HasBlockingIssues.Should().BeFalse();
        var entry = result.Write!.Entry;
        var ledger = new Meridian.Ledger.Ledger();

        ledger.Post(entry);

        CanonicalCashFlowTestData.AssertReportAndReversal(ledger, entry, 50_000m);
    }

    private static async Task<AccountingPostingCandidateWriteResult> BuildCashFlowDisposalCandidateAsync(
        decimal basis, bool compound = false, decimal? selectedBasis = null)
    {
        var lot = BuildFaceLotOfRecord(AuthoritativeFactorHarness.LedgerBookId, AuthoritativeFactorHarness.SecurityId,
            AuthoritativeFactorHarness.PositionId, 100m, .98m, 100m, pricePercentOfPar: basis);
        var harness = await CreateAuthoritativeFactorHarnessAsync([lot]);
        var eventType = AssetAccountingEventTypeNames.For(AssetAccountingEventKindDto.Disposal);
        var economic = harness.Request.EconomicEvent! with { EventType = eventType };
        var generated = new List<GeneratedPostingLineDto>
        {
            new("cash", "assets/cash", AccountingTemplateLineSideDto.Debit, "cash", 0m),
            new("carrying", "assets/investments", AccountingTemplateLineSideDto.Credit, "basis", 0m),
            new("realized", basis > 100m ? "expense/realized-loss" : "income/realized-gain",
                basis > 100m ? AccountingTemplateLineSideDto.Debit : AccountingTemplateLineSideDto.Credit, "realized", 0m)
        };
        var formulas = new List<AccountingRuleFormulaDto>
        {
            new("cash", AccountingRuleFormulaKindDto.FixedAmount, 100m),
            new("basis", AccountingRuleFormulaKindDto.FixedAmount, basis),
            new("realized", AccountingRuleFormulaKindDto.FixedAmount, Math.Abs(100m - basis)),
            new("accrual", AccountingRuleFormulaKindDto.FixedAmount, 25m)
        };
        if (compound)
        {
            await harness.Configuration.UpsertChartNodeAsync(new UpsertChartOfAccountsNodeRequest("fund-alpha",
                new ChartOfAccountsNodeDto("payable", "liabilities/payable", "Payable", "Liability"), "reviewer",
                LedgerBookId: AuthoritativeFactorHarness.LedgerBookId));
            generated.Add(new("accrual", "expense/realized-loss", AccountingTemplateLineSideDto.Debit, "accrual", 0m));
            generated.Add(new("payable", "liabilities/payable", AccountingTemplateLineSideDto.Credit, "accrual", 0m));
        }
        await harness.Configuration.UpsertPostingRuleAsync(new UpsertPostingRuleRequest("fund-alpha",
            new PostingRuleDto(AuthoritativeFactorHarness.RuleId, "Canonical disposal", eventType, "generated",
                RuleVersion: "v1", EffectiveFrom: new(2026, 1, 1), Priority: 100,
                Scope: new LedgerDimensionSetDto(FundId: "fund-alpha", EntityId: "entity-master"),
                Formulas: formulas, GeneratedPostings: generated), "reviewer",
            LedgerBookId: AuthoritativeFactorHarness.LedgerBookId));
        var request = harness.Request with
        {
            SourceEventType = eventType,
            EventAmount = 100m,
            EconomicEvent = economic,
            ProjectionLineage = harness.Request.ProjectionLineage! with { ModelKey = "security-disposal", TriggerEvent = economic },
            AssetLotMutation = new AssetLotMutationInstructionDto(AssetLotMutationIntentDto.Dispose,
                DisposalSelections: [new(lot.TaxLotRecordId, lot.LotId, lot.Version, lot.OpenQuantity,
                    lot.OpenQuantity, 0, harness.Request.RetainedEvidence[0].EvidenceId, lot.UnitCost, selectedBasis ?? basis)],
                ReliefMethod: "Fifo", PolicyRevision: "v1", AssetAccountId: "assets/investments")
        };
        return await harness.Service.BuildAuthoritativeCandidateWriteAsync(request,
            harness.Authority with { EventKind = AssetAccountingEventKindDto.Disposal });
    }
}
