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
        var ledgerBookId = Guid.NewGuid();
        var service = await CreateSeededCandidateServiceAsync(ledgerBookId: ledgerBookId);
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
            LedgerBookId: ledgerBookId,
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
        result.PostingCommand.LedgerBookId.Should().Be(ledgerBookId);
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
        var ledgerBookId = Guid.NewGuid();
        var service = await CreateSeededCandidateServiceAsync(ledgerBookId: ledgerBookId);

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
            LedgerBookId: ledgerBookId,
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
        var ledgerBookId = Guid.NewGuid();
        var service = await CreateSeededCandidateServiceAsync(
            incomeAccountType: "Memo",
            ledgerBookId: ledgerBookId);

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
            LedgerBookId: ledgerBookId,
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
        var ledgerBookId = Guid.NewGuid();
        var service = await CreateSeededCandidateServiceAsync(creditAmount: 120m, ledgerBookId: ledgerBookId);

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
            LedgerBookId: ledgerBookId,
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

    [Fact]
    public async Task BuildCandidateAsync_MissingLedgerBook_BlocksCandidateBeforeDraftWrite()
    {
        var service = await CreateSeededCandidateServiceAsync();

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
            "Missing ledger book",
            AccountingBasis: AccountingBasisKindDto.Gaap,
            Dimensions: new LedgerDimensionSetDto(FundId: "fund-alpha", EntityId: "entity-master"),
            CounterpartyId: "custodian-bny",
            PolicyId: "gaap-accrual-v1",
            TreatmentKind: AccountingTreatmentKindDto.Accrual,
            EvidenceLinks: ["provider://custodian/interest-accruals/2026-05"]));

        result.SelectedRuleId.Should().Be("posting.interest-accrual");
        result.GeneratedPostingLines.Should().HaveCount(2);
        result.PostingCommand.Should().BeNull();
        result.JournalEntryId.Should().BeNull();
        result.CanSubmitForApproval.Should().BeFalse();
        result.HasBlockingIssues.Should().BeTrue();
        result.Issues.Should().Contain(issue =>
            issue.Code == "posting-candidate.ledger-book-required" &&
            issue.BlocksCandidate &&
            issue.TargetId == "ledgerBookId");
    }

    [Fact]
    public async Task BuildCandidateAsync_PassesGeneratedLineDimensionsToGovernedDraftRequest()
    {
        var ledgerBookId = Guid.NewGuid();
        var configurationService = await CreateSeededConfigurationServiceAsync(ledgerBookId: ledgerBookId);
        var draftService = new CapturingJournalDraftService();
        var service = new AccountingPostingCandidateService(configurationService, draftService);
        var instrumentId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var sourceEventId = Guid.NewGuid();

        await service.BuildCandidateAsync(new PostingRuleJournalCandidateRequestDto(
            "fund-alpha",
            "CustodianInterestAccrual",
            125.44m,
            "USD",
            new DateOnly(2026, 5, 31),
            "controller@meridian.local",
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTimeOffset.Parse("2026-05-31T21:00:00Z"),
            "Dimensional generated posting",
            AccountingBasis: AccountingBasisKindDto.Gaap,
            LedgerBookId: ledgerBookId,
            Dimensions: new LedgerDimensionSetDto(
                FundId: "fund-alpha",
                EntityId: "entity-master",
                InstrumentId: instrumentId,
                ExternalGlDimensions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Department"] = "InvestmentOps"
                }),
            CounterpartyId: "custodian-bny",
            CorrelationId: correlationId,
            SourceEventId: sourceEventId,
            PolicyId: "gaap-accrual-v1",
            TreatmentKind: AccountingTreatmentKindDto.Accrual,
            EvidenceLinks: ["provider://custodian/interest-accruals/2026-05"]));

        draftService.CapturedRequest.Should().NotBeNull();
        draftService.CapturedRequest!.PostingRuleId.Should().Be("posting.interest-accrual");
        draftService.CapturedRequest.PostingRuleVersion.Should().Be("v1");
        draftService.CapturedRequest.LedgerBookId.Should().Be(ledgerBookId);
        draftService.CapturedRequest.DryRunCorrelationId.Should().Be(correlationId.ToString("D"));
        draftService.CapturedRequest.SourceEventId.Should().Be(sourceEventId);
        draftService.CapturedRequest!.Lines.Should().HaveCount(2);
        draftService.CapturedRequest.Lines.Should().OnlyContain(line => line.Dimensions != null);
        draftService.CapturedRequest.Lines.Should().OnlyContain(line => line.Dimensions!.FundId == "fund-alpha");
        draftService.CapturedRequest.Lines.Should().OnlyContain(line => line.Dimensions!.EntityId == "entity-master");
        draftService.CapturedRequest.Lines.Should().OnlyContain(line => line.Dimensions!.InstrumentId == instrumentId);
        draftService.CapturedRequest.Lines.Should().OnlyContain(line => line.Dimensions!.CounterpartyId == "custodian-bny");
        draftService.CapturedRequest.Lines.Should().Contain(line =>
            line.Credit == 125.44m &&
            line.Dimensions!.CostCenterId == "income-review" &&
            line.Dimensions.ExternalGlDimensions["Department"] == "InvestmentOps");
    }

    [Fact]
    public async Task BuildCandidateAsync_IsolatesRulesStudioWorkspaceByTenantAndCompany()
    {
        var ledgerBookId = Guid.NewGuid();
        var configurationService = new AccountingConfigurationService(
            new InMemoryAccountingConfigurationStore(),
            new InMemoryAccountingActionAuditStore());
        await SeedCandidateConfigurationAsync(
            configurationService,
            ledgerBookId,
            tenantId: "tenant-alpha",
            companyId: "company-alpha",
            ruleId: "posting.alpha-interest",
            creditAccountPath: "income/interest-alpha",
            creditNodeId: "interest-alpha",
            costCenterId: "alpha-review");
        await SeedCandidateConfigurationAsync(
            configurationService,
            ledgerBookId,
            tenantId: "tenant-beta",
            companyId: "company-beta",
            ruleId: "posting.beta-interest",
            creditAccountPath: "income/interest-beta",
            creditNodeId: "interest-beta",
            costCenterId: "beta-review");
        var draftService = new CapturingJournalDraftService();
        var service = new AccountingPostingCandidateService(configurationService, draftService);

        var alpha = await service.BuildCandidateAsync(new PostingRuleJournalCandidateRequestDto(
            "fund-alpha",
            "CustodianInterestAccrual",
            125.44m,
            "USD",
            new DateOnly(2026, 5, 31),
            "controller@meridian.local",
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTimeOffset.Parse("2026-05-31T21:00:00Z"),
            "Tenant-scoped generated posting",
            AccountingBasis: AccountingBasisKindDto.Gaap,
            LedgerBookId: ledgerBookId,
            Dimensions: new LedgerDimensionSetDto(FundId: "fund-alpha", EntityId: "entity-master"),
            CounterpartyId: "custodian-bny",
            PolicyId: "gaap-accrual-v1",
            TreatmentKind: AccountingTreatmentKindDto.Accrual,
            EvidenceLinks: ["provider://custodian/interest-accruals/2026-05"],
            TenantId: "tenant-alpha",
            CompanyId: "company-alpha"));
        var beta = await service.BuildCandidateAsync(new PostingRuleJournalCandidateRequestDto(
            "fund-alpha",
            "CustodianInterestAccrual",
            125.44m,
            "USD",
            new DateOnly(2026, 5, 31),
            "controller@meridian.local",
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTimeOffset.Parse("2026-05-31T21:00:00Z"),
            "Tenant-scoped generated posting",
            AccountingBasis: AccountingBasisKindDto.Gaap,
            LedgerBookId: ledgerBookId,
            Dimensions: new LedgerDimensionSetDto(FundId: "fund-alpha", EntityId: "entity-master"),
            CounterpartyId: "custodian-bny",
            PolicyId: "gaap-accrual-v1",
            TreatmentKind: AccountingTreatmentKindDto.Accrual,
            EvidenceLinks: ["provider://custodian/interest-accruals/2026-05"],
            TenantId: "tenant-beta",
            CompanyId: "company-beta"));
        var unscoped = await service.BuildCandidateAsync(new PostingRuleJournalCandidateRequestDto(
            "fund-alpha",
            "CustodianInterestAccrual",
            125.44m,
            "USD",
            new DateOnly(2026, 5, 31),
            "controller@meridian.local",
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTimeOffset.Parse("2026-05-31T21:00:00Z"),
            "Unscoped generated posting",
            AccountingBasis: AccountingBasisKindDto.Gaap,
            LedgerBookId: ledgerBookId,
            Dimensions: new LedgerDimensionSetDto(FundId: "fund-alpha", EntityId: "entity-master"),
            CounterpartyId: "custodian-bny",
            PolicyId: "gaap-accrual-v1",
            TreatmentKind: AccountingTreatmentKindDto.Accrual,
            EvidenceLinks: ["provider://custodian/interest-accruals/2026-05"]));

        alpha.SelectedRuleId.Should().Be("posting.alpha-interest");
        alpha.GeneratedPostingLines.Should().Contain(line =>
            line.AccountPath == "income/interest-alpha" &&
            line.Dimensions!.CostCenterId == "alpha-review");
        alpha.GeneratedPostingLines.Should().NotContain(line => line.AccountPath == "income/interest-beta");
        beta.SelectedRuleId.Should().Be("posting.beta-interest");
        beta.GeneratedPostingLines.Should().Contain(line =>
            line.AccountPath == "income/interest-beta" &&
            line.Dimensions!.CostCenterId == "beta-review");
        beta.GeneratedPostingLines.Should().NotContain(line => line.AccountPath == "income/interest-alpha");
        unscoped.SelectedRuleId.Should().BeNull();
        unscoped.PostingCommand.Should().BeNull();
        unscoped.Issues.Should().Contain(issue =>
            issue.Code == "posting-candidate.rule-required" &&
            issue.BlocksCandidate);
    }

    private static async Task<AccountingPostingCandidateService> CreateSeededCandidateServiceAsync(
        string incomeAccountType = "Revenue",
        decimal? creditAmount = null,
        Guid? ledgerBookId = null)
    {
        var configurationService = await CreateSeededConfigurationServiceAsync(incomeAccountType, creditAmount, ledgerBookId);
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

        return new AccountingPostingCandidateService(
            configurationService,
            new AccountingJournalDraftService(
                policyService,
                new AccountingBasisProjectionService(policyService)));
    }

    private static async Task<AccountingConfigurationService> CreateSeededConfigurationServiceAsync(
        string incomeAccountType = "Revenue",
        decimal? creditAmount = null,
        Guid? ledgerBookId = null)
    {
        var configurationService = new AccountingConfigurationService(
            new InMemoryAccountingConfigurationStore(),
            new InMemoryAccountingActionAuditStore());

        await SeedCandidateConfigurationAsync(
            configurationService,
            ledgerBookId,
            incomeAccountType,
            creditAmount);

        return configurationService;
    }

    private static async Task SeedCandidateConfigurationAsync(
        AccountingConfigurationService configurationService,
        Guid? ledgerBookId,
        string incomeAccountType = "Revenue",
        decimal? creditAmount = null,
        string? tenantId = null,
        string? companyId = null,
        string ruleId = "posting.interest-accrual",
        string creditAccountPath = "income/interest",
        string creditNodeId = "interest-income",
        string costCenterId = "income-review")
    {
        await configurationService.UpsertChartNodeAsync(new UpsertChartOfAccountsNodeRequest(
            "fund-alpha",
            new ChartOfAccountsNodeDto(
                "accrued-interest",
                "assets/accrued-interest",
                "Accrued Interest Receivable",
                "Asset"),
            "controller@meridian.local",
            CompanyId: companyId,
            LedgerBookId: ledgerBookId,
            TenantId: tenantId));
        await configurationService.UpsertChartNodeAsync(new UpsertChartOfAccountsNodeRequest(
            "fund-alpha",
            new ChartOfAccountsNodeDto(
                creditNodeId,
                creditAccountPath,
                "Interest Income",
                incomeAccountType),
            "controller@meridian.local",
            CompanyId: companyId,
            LedgerBookId: ledgerBookId,
            TenantId: tenantId));
        await configurationService.UpsertPostingRuleAsync(new UpsertPostingRuleRequest(
            "fund-alpha",
            new PostingRuleDto(
                ruleId,
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
                        creditNodeId,
                        creditAccountPath,
                        AccountingTemplateLineSideDto.Credit,
                        creditAmount is null ? "source-amount" : "fixed-credit",
                        creditAmount ?? 0m,
                        "USD",
                        new LedgerDimensionSetDto(CostCenterId: costCenterId),
                        "Credit interest income")
                ]),
            "controller@meridian.local",
            CompanyId: companyId,
            LedgerBookId: ledgerBookId,
            TenantId: tenantId));
    }

    private sealed class CapturingJournalDraftService : IAccountingJournalDraftService
    {
        public AccountingJournalDraftRequest? CapturedRequest { get; private set; }

        public Task<AccountingJournalDraftResult> BuildDraftAsync(
            AccountingJournalDraftRequest request,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            CapturedRequest = request;
            var totalDebits = request.Lines.Sum(static line => line.Debit);
            var totalCredits = request.Lines.Sum(static line => line.Credit);
            var policy = new AccountingPolicyDto(
                "captured-policy",
                request.AccountingBasis,
                "v1",
                "Captured policy",
                new DateOnly(2026, 1, 1),
                EffectiveTo: null,
                IsDefault: true,
                RulesJson: "{}",
                CreatedAt: DateTimeOffset.Parse("2026-01-01T00:00:00Z"));

            return Task.FromResult(new AccountingJournalDraftResult(
                policy,
                Rule: null,
                DraftEntry: null,
                Write: null,
                totalDebits,
                totalCredits,
                totalDebits - totalCredits,
                totalDebits == totalCredits,
                HasCriticalIssues: false,
                CanSubmitForApproval: false,
                CanPostWithoutAdditionalApproval: false,
                request.EvidenceLinks ?? [],
                ValidationIssues: []));
        }
    }
}
