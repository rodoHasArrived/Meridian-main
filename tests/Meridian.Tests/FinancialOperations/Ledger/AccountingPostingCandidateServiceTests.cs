using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Ledger;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.FinancialOperations.Ledger;

/// <summary>
/// Guards the fund-accounting source-event scenario where promoted posting rules may draft
/// journal impact, but posting remains behind retained evidence and approval gates.
/// </summary>
public sealed class AccountingPostingCandidateServiceTests
{
    [Fact]
    public async Task Scenario_AccountingRulesStudio_SourceEventBuildsApprovalGatedJournalCandidate()
    {
        var service = await CreateSeededCandidateServiceAsync();
        var instrumentId = Guid.NewGuid();
        var sourceEventId = Guid.NewGuid();
        var aggregateId = Guid.NewGuid();
        var periodId = Guid.NewGuid();

        var result = await service.BuildCandidateAsync(new PostingRuleJournalCandidateRequestDto(
            "fund-alpha",
            "CustodianInterestAccrual",
            125.44m,
            "usd",
            new DateOnly(2026, 5, 31),
            "controller@meridian.local",
            aggregateId,
            periodId,
            DateTimeOffset.Parse("2026-05-31T21:00:00Z"),
            "Accrue custodian interest from retained source event",
            AccountingBasis: AccountingBasisKindDto.Gaap,
            Dimensions: new LedgerDimensionSetDto(
                FundId: "fund-alpha",
                EntityId: "entity-master",
                InstrumentId: instrumentId,
                ExternalGlDimensions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Department"] = "InvestmentOps"
                }),
            CounterpartyId: "custodian-bny",
            InstrumentSymbol: "TBILL",
            CorrelationId: Guid.NewGuid(),
            SourceEventId: sourceEventId,
            PolicyId: "gaap-accrual-v1",
            TreatmentKind: AccountingTreatmentKindDto.Accrual,
            TreasuryContext: new TreasuryLedgerContextDto(
                EffectiveDate: new DateOnly(2026, 5, 31),
                IdempotencyKey: "custodian-interest:fund-alpha:202605",
                FundEventId: "fund-event:fund-alpha:interest-accrual:202605",
                FundEventType: "InterestAccrual",
                CapitalAccountId: "capital-account:fund-alpha:master",
                InvestorId: "investor:fund-alpha:master",
                PaymentIntentId: "payment:fund-alpha:interest-accrual:202605",
                SettlementReference: "settlement:fund-alpha:interest-accrual:202605"),
            EvidenceLinks: ["provider://custodian/interest-accruals/2026-05"]));

        result.SelectedRuleId.Should().Be("posting.interest-accrual");
        result.SelectedRuleVersion.Should().Be("v1");
        result.GeneratedPostingLines.Should().HaveCount(2);
        result.GeneratedPostingLines.Should().OnlyContain(line => line.Dimensions != null);
        result.GeneratedPostingLines.Should().OnlyContain(line => line.Dimensions!.FundId == "fund-alpha");
        result.GeneratedPostingLines.Should().OnlyContain(line => line.Dimensions!.EntityId == "entity-master");
        result.GeneratedPostingLines.Should().OnlyContain(line => line.Dimensions!.InstrumentId == instrumentId);
        result.GeneratedPostingLines.Should().OnlyContain(line => line.Dimensions!.CounterpartyId == "custodian-bny");
        result.GeneratedPostingLines.Should().Contain(line =>
            line.Dimensions!.CostCenterId == "income-review" &&
            line.Dimensions.ExternalGlDimensions["Department"] == "InvestmentOps");
        result.TotalDebits.Should().Be(125.44m);
        result.TotalCredits.Should().Be(125.44m);
        result.IsBalanced.Should().BeTrue();
        result.HasBlockingIssues.Should().BeFalse();
        result.CanSubmitForApproval.Should().BeTrue();
        result.CanPostWithoutAdditionalApproval.Should().BeFalse();
        result.PostingCommand.Should().NotBeNull();
        result.PostingCommand!.AggregateId.Should().Be(aggregateId);
        result.PostingCommand.PeriodId.Should().Be(periodId);
        result.PostingCommand.SourceEventId.Should().Be(sourceEventId);
        result.PostingCommand.SourceEventType.Should().Be("CustodianInterestAccrual");
        result.PostingCommand.ApprovalState.Should().Be(AccountingPostingApprovalStateDto.Pending);
        result.PostingCommand.Evidence.Should().ContainSingle(evidence =>
            evidence.Uri == "provider://custodian/interest-accruals/2026-05");
        result.EvidenceLinks.Should().ContainSingle("provider://custodian/interest-accruals/2026-05");
        result.Issues.Should().Contain(issue =>
            issue.Code == "JOURNAL_DRAFT_APPROVAL_REQUIRED" &&
            !issue.BlocksCandidate);
    }

    [Fact]
    public async Task BuildCandidateAsync_NoMatchingRule_BlocksCandidateWithoutPostingCommand()
    {
        var service = await CreateSeededCandidateServiceAsync();

        var result = await service.BuildCandidateAsync(new PostingRuleJournalCandidateRequestDto(
            "fund-alpha",
            "UnmappedDividendEvent",
            50m,
            "USD",
            new DateOnly(2026, 5, 31),
            "controller@meridian.local",
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTimeOffset.Parse("2026-05-31T21:00:00Z"),
            "Unmapped dividend event",
            PolicyId: "gaap-accrual-v1",
            EvidenceLinks: ["provider://custodian/dividends/2026-05"]));

        result.SelectedRuleId.Should().BeNull();
        result.PostingCommand.Should().BeNull();
        result.CanSubmitForApproval.Should().BeFalse();
        result.HasBlockingIssues.Should().BeTrue();
        result.Issues.Should().Contain(issue =>
            issue.Code == "rule.none" &&
            issue.BlocksCandidate);
        result.Issues.Should().Contain(issue =>
            issue.Code == "posting-candidate.rule-required" &&
            issue.BlocksCandidate);
    }

    [Fact]
    public async Task BuildCandidateAsync_UnsupportedChartAccountType_BlocksCandidate()
    {
        var service = await CreateSeededCandidateServiceAsync(
            incomeAccountType: "Memo");

        var result = await service.BuildCandidateAsync(new PostingRuleJournalCandidateRequestDto(
            "fund-alpha",
            "CustodianInterestAccrual",
            125.44m,
            "USD",
            new DateOnly(2026, 5, 31),
            "controller@meridian.local",
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTimeOffset.Parse("2026-05-31T21:00:00Z"),
            "Unsupported account type",
            AccountingBasis: AccountingBasisKindDto.Gaap,
            Dimensions: new LedgerDimensionSetDto(FundId: "fund-alpha", EntityId: "entity-master"),
            CounterpartyId: "custodian-bny",
            PolicyId: "gaap-accrual-v1",
            TreatmentKind: AccountingTreatmentKindDto.Accrual,
            EvidenceLinks: ["provider://custodian/interest-accruals/2026-05"]));

        result.GeneratedPostingLines.Should().HaveCount(2);
        result.PostingCommand.Should().BeNull();
        result.CanSubmitForApproval.Should().BeFalse();
        result.HasBlockingIssues.Should().BeTrue();
        result.Issues.Should().Contain(issue =>
            issue.Code == "posting-candidate.account-type-unsupported" &&
            issue.BlocksCandidate &&
            issue.TargetId == "interest-income");
    }

    [Fact]
    public async Task BuildCandidateAsync_UnbalancedGeneratedLines_BlocksCandidate()
    {
        var service = await CreateSeededCandidateServiceAsync(creditAmount: 120m);

        var result = await service.BuildCandidateAsync(new PostingRuleJournalCandidateRequestDto(
            "fund-alpha",
            "CustodianInterestAccrual",
            125.44m,
            "USD",
            new DateOnly(2026, 5, 31),
            "controller@meridian.local",
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTimeOffset.Parse("2026-05-31T21:00:00Z"),
            "Unbalanced generated posting",
            AccountingBasis: AccountingBasisKindDto.Gaap,
            Dimensions: new LedgerDimensionSetDto(FundId: "fund-alpha", EntityId: "entity-master"),
            CounterpartyId: "custodian-bny",
            PolicyId: "gaap-accrual-v1",
            TreatmentKind: AccountingTreatmentKindDto.Accrual,
            EvidenceLinks: ["provider://custodian/interest-accruals/2026-05"]));

        result.TotalDebits.Should().Be(125.44m);
        result.TotalCredits.Should().Be(120m);
        result.IsBalanced.Should().BeFalse();
        result.PostingCommand.Should().BeNull();
        result.CanSubmitForApproval.Should().BeFalse();
        result.HasBlockingIssues.Should().BeTrue();
        result.Issues.Should().Contain(issue =>
            issue.Code == "rule.generated-unbalanced" &&
            issue.BlocksCandidate);
    }

    private static async Task<AccountingPostingCandidateService> CreateSeededCandidateServiceAsync(
        string incomeAccountType = "Revenue",
        decimal? creditAmount = null)
    {
        var configurationService = new AccountingConfigurationService(
            new InMemoryAccountingConfigurationStore(),
            new InMemoryAccountingActionAuditStore());
        var policyService = new AccountingPolicyService();
        await policyService.CreatePolicyAsync(new CreateAccountingPolicyRequest(
            AccountingBasisKindDto.Gaap,
            PolicyId: "gaap-accrual-v1",
            Version: "v1",
            DisplayName: "GAAP accrual treatment",
            EffectiveFrom: new DateOnly(2026, 1, 1),
            RulePack: new AccountingPolicyRulePackDto(
                "gaap-accrual-rules",
                "v1",
                [
                    new AccountingPolicyRuleDto(
                        "accrual.interest-income",
                        AccountingTreatmentKindDto.Accrual,
                        RuleVersion: "v1",
                        SourceEventType: "CustodianInterestAccrual",
                        RequiresEvidence: true,
                        RequiresApproval: true,
                        AllowsAutoPosting: false,
                        Description: "Accrue custodian interest income from retained source evidence.")
                ])));

        await configurationService.UpsertChartNodeAsync(new UpsertChartOfAccountsNodeRequest(
            "fund-alpha",
            new ChartOfAccountsNodeDto(
                "accrued-interest",
                "assets/accrued-interest",
                "Accrued Interest Receivable",
                "Asset"),
            "controller@meridian.local"));
        await configurationService.UpsertChartNodeAsync(new UpsertChartOfAccountsNodeRequest(
            "fund-alpha",
            new ChartOfAccountsNodeDto(
                "interest-income",
                "income/interest",
                "Interest Income",
                incomeAccountType),
            "controller@meridian.local"));
        await configurationService.UpsertPostingRuleAsync(new UpsertPostingRuleRequest(
            "fund-alpha",
            new PostingRuleDto(
                "posting.interest-accrual",
                "Custodian interest accrual",
                "CustodianInterestAccrual",
                TemplateId: "generated",
                RuleVersion: "v1",
                EffectiveFrom: new DateOnly(2026, 1, 1),
                Priority: 100,
                Scope: new LedgerDimensionSetDto(FundId: "fund-alpha", EntityId: "entity-master"),
                Conditions:
                [
                    new AccountingRuleConditionDto(
                        "minimum-accrual",
                        "eventAmount",
                        AccountingRuleConditionOperatorDto.AmountGreaterThanOrEqual,
                        "100")
                ],
                Formulas:
                [
                    new AccountingRuleFormulaDto(
                        "source-amount",
                        AccountingRuleFormulaKindDto.SourceAmount,
                        0m),
                    new AccountingRuleFormulaDto(
                        "fixed-credit",
                        AccountingRuleFormulaKindDto.FixedAmount,
                        creditAmount ?? 0m)
                ],
                GeneratedPostings:
                [
                    new GeneratedPostingLineDto(
                        "accrued-interest",
                        "assets/accrued-interest",
                        AccountingTemplateLineSideDto.Debit,
                        "source-amount",
                        0m,
                        "USD",
                        Description: "Debit accrued interest receivable"),
                    new GeneratedPostingLineDto(
                        "interest-income",
                        "income/interest",
                        AccountingTemplateLineSideDto.Credit,
                        creditAmount is null ? "source-amount" : "fixed-credit",
                        creditAmount ?? 0m,
                        "USD",
                        new LedgerDimensionSetDto(CostCenterId: "income-review"),
                        "Credit interest income")
                ]),
            "controller@meridian.local"));

        return new AccountingPostingCandidateService(
            configurationService,
            new AccountingJournalDraftService(
                policyService,
                new AccountingBasisProjectionService(policyService)));
    }
}
