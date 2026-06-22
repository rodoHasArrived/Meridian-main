using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
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

    [Fact]
    public async Task PostCandidateAsync_ApprovedGeneratedCandidateAppendsDurableLedgerWrite()
    {
        var ledgerBookId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var sourceEventId = Guid.NewGuid();
        var candidateService = await CreateSeededCandidateServiceAsync(ledgerBookId: ledgerBookId);
        var store = new RecordingLedgerJournalStore(
            BuildLedgerBook(ledgerBookId, AccountingBasisKindDto.Gaap),
            BuildPeriod(periodId, ledgerBookId));
        var service = new AccountingPostingCandidatePostService(candidateService, store);

        var result = await service.PostCandidateAsync(new PostPostingRuleJournalCandidateRequestDto(
            BuildCandidateRequest(
                ledgerBookId,
                periodId,
                sourceEventId,
                AccountingBasisKindDto.Gaap,
                "gaap-accrual-v1"),
            "reviewer@meridian.local",
            "approval-generated-interest-202605",
            "Reviewed retained custodian source event.",
            EvidenceLinks: ["approval://workpaper/generated-interest-202605"]));

        result.WasReplay.Should().BeFalse();
        result.Candidate.PostingCommand.Should().NotBeNull();
        result.Candidate.PostingCommand!.ApprovalState.Should().Be(AccountingPostingApprovalStateDto.Approved);
        result.PostedJournal.LedgerBookId.Should().Be(ledgerBookId);
        result.PostedJournal.AggregateId.Should().Be(ledgerBookId);
        result.PostedJournal.SourceEventId.Should().Be(sourceEventId);
        store.Appended.Should().ContainSingle();
        var appended = store.Appended.Single();
        appended.AggregateId.Should().Be(ledgerBookId);
        appended.LedgerBookId.Should().Be(ledgerBookId);
        appended.SourceEventId.Should().Be(sourceEventId);
        appended.PostingCommand.Should().NotBeNull();
        appended.PostingCommand!.AggregateId.Should().Be(ledgerBookId);
        appended.PostingCommand.LedgerBookId.Should().Be(ledgerBookId);
        appended.PostingCommand.SourceEventId.Should().Be(sourceEventId);
        appended.PostingCommand.ApprovalState.Should().Be(AccountingPostingApprovalStateDto.Approved);
        appended.PostingCommand.Evidence.Should().Contain(evidence =>
            evidence.Kind == AccountingPostingEvidenceKindDto.Approval &&
            evidence.EvidenceId == "approval-generated-interest-202605");
    }

    [Fact]
    public async Task PostCandidateAsync_SameEconomicEventCanPostSeparateBasisLedgerBooks()
    {
        var gaapLedgerBookId = Guid.NewGuid();
        var cashLedgerBookId = Guid.NewGuid();
        var gaapPeriodId = Guid.NewGuid();
        var cashPeriodId = Guid.NewGuid();
        var sourceEventId = Guid.NewGuid();
        var candidateService = await CreateSeededCandidateServiceForBooksAsync(
            [gaapLedgerBookId, cashLedgerBookId],
            [AccountingBasisKindDto.Gaap, AccountingBasisKindDto.Cash]);
        var store = new RecordingLedgerJournalStore(
            [
                BuildLedgerBook(gaapLedgerBookId, AccountingBasisKindDto.Gaap),
                BuildLedgerBook(cashLedgerBookId, AccountingBasisKindDto.Cash)
            ],
            [
                BuildPeriod(gaapPeriodId, gaapLedgerBookId),
                BuildPeriod(cashPeriodId, cashLedgerBookId)
            ]);
        var service = new AccountingPostingCandidatePostService(candidateService, store);

        var gaap = await service.PostCandidateAsync(new PostPostingRuleJournalCandidateRequestDto(
            BuildCandidateRequest(
                gaapLedgerBookId,
                gaapPeriodId,
                sourceEventId,
                AccountingBasisKindDto.Gaap,
                "gaap-accrual-v1"),
            "reviewer@meridian.local",
            "approval-gaap-interest-202605",
            EvidenceLinks: ["approval://workpaper/gaap-interest-202605"]));
        var cash = await service.PostCandidateAsync(new PostPostingRuleJournalCandidateRequestDto(
            BuildCandidateRequest(
                cashLedgerBookId,
                cashPeriodId,
                sourceEventId,
                AccountingBasisKindDto.Cash,
                "cash-accrual-v1"),
            "reviewer@meridian.local",
            "approval-cash-interest-202605",
            EvidenceLinks: ["approval://workpaper/cash-interest-202605"]));

        gaap.PostedJournal.SourceEventId.Should().Be(sourceEventId);
        cash.PostedJournal.SourceEventId.Should().Be(sourceEventId);
        gaap.PostedJournal.LedgerBookId.Should().Be(gaapLedgerBookId);
        cash.PostedJournal.LedgerBookId.Should().Be(cashLedgerBookId);
        gaap.PostedJournal.AggregateId.Should().Be(gaapLedgerBookId);
        cash.PostedJournal.AggregateId.Should().Be(cashLedgerBookId);
        store.Appended.Should().HaveCount(2);
        store.Appended.Select(static write => write.AggregateId).Should().BeEquivalentTo([gaapLedgerBookId, cashLedgerBookId]);
        store.Appended.Should().OnlyContain(write => write.SourceEventId == sourceEventId);
    }

    [Fact]
    public async Task PostCandidateAsync_ReplayedSourceEventForSameBookReturnsExistingJournal()
    {
        var ledgerBookId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var sourceEventId = Guid.NewGuid();
        var candidateService = await CreateSeededCandidateServiceAsync(ledgerBookId: ledgerBookId);
        var store = new RecordingLedgerJournalStore(
            BuildLedgerBook(ledgerBookId, AccountingBasisKindDto.Gaap),
            BuildPeriod(periodId, ledgerBookId));
        var service = new AccountingPostingCandidatePostService(candidateService, store);
        var request = new PostPostingRuleJournalCandidateRequestDto(
            BuildCandidateRequest(
                ledgerBookId,
                periodId,
                sourceEventId,
                AccountingBasisKindDto.Gaap,
                "gaap-accrual-v1"),
            "reviewer@meridian.local",
            "approval-generated-interest-202605",
            EvidenceLinks: ["approval://workpaper/generated-interest-202605"]);

        var first = await service.PostCandidateAsync(request);
        var second = await service.PostCandidateAsync(request);

        second.WasReplay.Should().BeTrue();
        second.PostedJournal.JournalEntryId.Should().Be(first.PostedJournal.JournalEntryId);
        second.PostedJournal.SourceEventId.Should().Be(sourceEventId);
        store.Appended.Should().ContainSingle();
    }

    [Fact]
    public async Task PostCandidateAsync_AggregateMustEqualLedgerBook()
    {
        var ledgerBookId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var sourceEventId = Guid.NewGuid();
        var candidateService = await CreateSeededCandidateServiceAsync(ledgerBookId: ledgerBookId);
        var store = new RecordingLedgerJournalStore(
            BuildLedgerBook(ledgerBookId, AccountingBasisKindDto.Gaap),
            BuildPeriod(periodId, ledgerBookId));
        var service = new AccountingPostingCandidatePostService(candidateService, store);
        var request = BuildCandidateRequest(
            ledgerBookId,
            periodId,
            sourceEventId,
            AccountingBasisKindDto.Gaap,
            "gaap-accrual-v1") with
            {
                AggregateId = sourceEventId
            };

        var act = () => service.PostCandidateAsync(new PostPostingRuleJournalCandidateRequestDto(
            request,
            "reviewer@meridian.local",
            "approval-generated-interest-202605"));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*aggregate id must equal the target ledger book id*");
        store.Appended.Should().BeEmpty();
    }

    [Fact]
    public async Task PostCandidateAsync_HardClosedPeriodBlocksGeneratedPostingBeforeAppend()
    {
        var ledgerBookId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var sourceEventId = Guid.NewGuid();
        var candidateService = await CreateSeededCandidateServiceAsync(ledgerBookId: ledgerBookId);
        var store = new RecordingLedgerJournalStore(
            BuildLedgerBook(ledgerBookId, AccountingBasisKindDto.Gaap),
            BuildPeriod(periodId, ledgerBookId, "HardClosed"));
        var service = new AccountingPostingCandidatePostService(candidateService, store);

        var act = () => service.PostCandidateAsync(new PostPostingRuleJournalCandidateRequestDto(
            BuildCandidateRequest(
                ledgerBookId,
                periodId,
                sourceEventId,
                AccountingBasisKindDto.Gaap,
                "gaap-accrual-v1"),
            "reviewer@meridian.local",
            "approval-generated-interest-202605",
            EvidenceLinks: ["approval://workpaper/generated-interest-202605"]));

        await act.Should().ThrowAsync<LedgerValidationException>()
            .WithMessage("*hard-closed; no postings are permitted*");
        store.Appended.Should().BeEmpty();
    }

    [Fact]
    public async Task PostCandidateAsync_WithoutLedgerStoreFailsClosed()
    {
        var ledgerBookId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var sourceEventId = Guid.NewGuid();
        var candidateService = await CreateSeededCandidateServiceAsync(ledgerBookId: ledgerBookId);
        var service = new AccountingPostingCandidatePostService(candidateService);

        var act = () => service.PostCandidateAsync(new PostPostingRuleJournalCandidateRequestDto(
            BuildCandidateRequest(
                ledgerBookId,
                periodId,
                sourceEventId,
                AccountingBasisKindDto.Gaap,
                "gaap-accrual-v1"),
            "reviewer@meridian.local",
            "approval-generated-interest-202605"));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Postgres-backed ledger journal store*");
    }

    private static async Task<AccountingPostingCandidateService> CreateSeededCandidateServiceAsync(
        string incomeAccountType = "Revenue",
        decimal? creditAmount = null,
        Guid? ledgerBookId = null)
    {
        var configurationService = await CreateSeededConfigurationServiceAsync(incomeAccountType, creditAmount, ledgerBookId);
        var policyService = await CreatePolicyServiceAsync([AccountingBasisKindDto.Gaap]);

        return new AccountingPostingCandidateService(
            configurationService,
            new AccountingJournalDraftService(
                policyService,
                new AccountingBasisProjectionService(policyService)));
    }

    private static async Task<AccountingPostingCandidateService> CreateSeededCandidateServiceForBooksAsync(
        IReadOnlyList<Guid> ledgerBookIds,
        IReadOnlyList<AccountingBasisKindDto> policyBases)
    {
        var configurationService = new AccountingConfigurationService(
            new InMemoryAccountingConfigurationStore(),
            new InMemoryAccountingActionAuditStore());
        foreach (var ledgerBookId in ledgerBookIds)
        {
            await SeedCandidateConfigurationAsync(configurationService, ledgerBookId);
        }

        var policyService = await CreatePolicyServiceAsync(policyBases);
        return new AccountingPostingCandidateService(
            configurationService,
            new AccountingJournalDraftService(
                policyService,
                new AccountingBasisProjectionService(policyService)));
    }

    private static async Task<AccountingPolicyService> CreatePolicyServiceAsync(
        IReadOnlyList<AccountingBasisKindDto> policyBases)
    {
        var policyService = new AccountingPolicyService();
        foreach (var basis in policyBases.Distinct())
        {
            await policyService.CreatePolicyAsync(new CreateAccountingPolicyRequest(
                basis,
                PolicyId: PolicyIdForBasis(basis),
                Version: "v1",
                DisplayName: $"{basis} accrual treatment",
                EffectiveFrom: new DateOnly(2026, 1, 1),
                RulePack: new AccountingPolicyRulePackDto(
                    $"{basis.ToString().ToLowerInvariant()}-accrual-rules",
                    "v1",
                    [
                        new AccountingPolicyRuleDto(
                            $"accrual.interest-income.{basis.ToString().ToLowerInvariant()}",
                            AccountingTreatmentKindDto.Accrual,
                            RuleVersion: "v1",
                            SourceEventType: "CustodianInterestAccrual",
                            RequiresEvidence: true,
                            RequiresApproval: true,
                            AllowsAutoPosting: false,
                            Description: "Accrue custodian interest income from retained source evidence.")
                    ])));
        }

        return policyService;
    }

    private static string PolicyIdForBasis(AccountingBasisKindDto basis)
        => basis switch
        {
            AccountingBasisKindDto.Gaap => "gaap-accrual-v1",
            AccountingBasisKindDto.Cash => "cash-accrual-v1",
            AccountingBasisKindDto.Tax => "tax-accrual-v1",
            AccountingBasisKindDto.Statutory => "statutory-accrual-v1",
            _ => "primary-accrual-v1"
        };

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

    private static PostingRuleJournalCandidateRequestDto BuildCandidateRequest(
        Guid ledgerBookId,
        Guid periodId,
        Guid sourceEventId,
        AccountingBasisKindDto accountingBasis,
        string policyId)
        => new(
            "fund-alpha",
            "CustodianInterestAccrual",
            125.44m,
            "USD",
            new DateOnly(2026, 5, 31),
            "controller@meridian.local",
            ledgerBookId,
            periodId,
            DateTimeOffset.Parse("2026-05-31T21:00:00Z"),
            "Accrue custodian interest from retained source event",
            AccountingBasis: accountingBasis,
            LedgerBookId: ledgerBookId,
            Dimensions: new LedgerDimensionSetDto(FundId: "fund-alpha", EntityId: "entity-master"),
            CounterpartyId: "custodian-bny",
            CorrelationId: Guid.NewGuid(),
            SourceEventId: sourceEventId,
            PolicyId: policyId,
            TreatmentKind: AccountingTreatmentKindDto.Accrual,
            TreasuryContext: new TreasuryLedgerContextDto(
                EffectiveDate: new DateOnly(2026, 5, 31),
                IdempotencyKey: $"custodian-interest:{ledgerBookId:N}:{sourceEventId:N}",
                FundEventId: $"fund-event:fund-alpha:interest-accrual:{sourceEventId:N}",
                FundEventType: "InterestAccrual",
                CapitalAccountId: "capital-account:fund-alpha:master",
                InvestorId: "investor:fund-alpha:master",
                PaymentIntentId: $"payment:fund-alpha:interest-accrual:{sourceEventId:N}",
                SettlementReference: $"settlement:fund-alpha:interest-accrual:{sourceEventId:N}"),
            EvidenceLinks: ["provider://custodian/interest-accruals/2026-05"]);

    private static LedgerBookRecord BuildLedgerBook(Guid ledgerBookId, AccountingBasisKindDto accountingBasis)
        => new(
            ledgerBookId,
            "fund-alpha",
            Guid.NewGuid(),
            FundStructureNodeKindDto.Fund,
            $"{accountingBasis} ledger",
            "USD",
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            AccountingBasis: accountingBasis,
            AccountingPolicyId: PolicyIdForBasis(accountingBasis),
            AccountingPolicyVersion: "v1");

    private static LedgerAccountingPeriod BuildPeriod(Guid periodId, Guid ledgerBookId, string status = "Open")
        => new(
            periodId,
            ledgerBookId,
            2026,
            5,
            "May 2026",
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31),
            status,
            DateTimeOffset.Parse("2026-05-01T00:00:00Z"),
            ClosedAt: string.Equals(status, "Open", StringComparison.Ordinal)
                ? null
                : DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
            Version: 1);

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

    private sealed class RecordingLedgerJournalStore : ILedgerJournalStore
    {
        private readonly Dictionary<Guid, LedgerBookRecord> _books;
        private readonly Dictionary<Guid, LedgerAccountingPeriod> _periods;
        private readonly List<LedgerJournalEntryRecord> _records = [];
        private long _sequence;

        public RecordingLedgerJournalStore(
            LedgerBookRecord book,
            LedgerAccountingPeriod period)
            : this([book], [period])
        {
        }

        public RecordingLedgerJournalStore(
            IReadOnlyList<LedgerBookRecord> books,
            IReadOnlyList<LedgerAccountingPeriod> periods)
        {
            _books = books.ToDictionary(static book => book.LedgerBookId);
            _periods = periods.ToDictionary(static period => period.PeriodId);
        }

        public List<LedgerJournalEntryWrite> Appended { get; } = [];

        public Task AppendAsync(LedgerJournalEntryWrite entry, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var normalized = AccountingPostingCommandValidator.NormalizeAndValidate(entry);
            if (!normalized.LedgerBookId.HasValue || !_books.TryGetValue(normalized.LedgerBookId.Value, out var book))
            {
                throw new InvalidOperationException("Ledger book was not found.");
            }

            if (!_periods.TryGetValue(normalized.PeriodId, out var period))
            {
                throw new InvalidOperationException("Ledger period was not found.");
            }

            if (period.LedgerBookId != book.LedgerBookId)
            {
                throw new InvalidOperationException("Ledger period does not belong to the ledger book.");
            }

            if (book.AccountingBasis != normalized.AccountingBasis)
            {
                throw new InvalidOperationException("Journal basis does not match ledger book basis.");
            }

            if (_records.Any(record =>
                    record.AggregateId == normalized.AggregateId &&
                    record.SourceEventId == normalized.SourceEventId &&
                    normalized.SourceEventId.HasValue))
            {
                throw new InvalidOperationException("Duplicate source event posting.");
            }

            Appended.Add(normalized);
            _records.Add(new LedgerJournalEntryRecord(
                normalized.Entry,
                normalized.AggregateId,
                normalized.PeriodId,
                normalized.CommandId,
                normalized.CorrelationId,
                ++_sequence,
                DateTimeOffset.UtcNow,
                normalized.AccountingBasis,
                normalized.AccountingPolicyId,
                normalized.AccountingPolicyVersion,
                normalized.RuleId,
                normalized.RuleVersion,
                normalized.SourceEventId,
                normalized.SourceJournalEntryId,
                normalized.PostingKind,
                normalized.AdjustmentApproval));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<LedgerJournalEntryRecord>> GetByPeriodAsync(Guid periodId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<LedgerJournalEntryRecord>>(_records.Where(record => record.PeriodId == periodId).ToArray());

        public Task<IReadOnlyList<LedgerJournalEntryRecord>> GetByAggregateAsync(Guid aggregateId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<LedgerJournalEntryRecord>>(_records.Where(record => record.AggregateId == aggregateId).ToArray());

        public Task<LedgerAccountingPeriod?> GetPeriodAsync(Guid periodId, CancellationToken ct = default)
            => Task.FromResult(_periods.GetValueOrDefault(periodId));

        public Task<IReadOnlyList<LedgerAccountingPeriod>> ListPeriodsAsync(
            Guid? ledgerBookId = null,
            string? status = null,
            string? fundProfileId = null,
            Guid? fundStructureNodeId = null,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<LedgerAccountingPeriod>>(_periods.Values
                .Where(period => !ledgerBookId.HasValue || period.LedgerBookId == ledgerBookId)
                .ToArray());

        public Task<LedgerAccountingPeriod> SavePeriodAsync(
            LedgerAccountingPeriod period,
            long expectedVersion,
            PeriodCloseEventRecord? closeEvent = null,
            CancellationToken ct = default)
        {
            _periods[period.PeriodId] = period;
            return Task.FromResult(period);
        }

        public Task<LedgerBookRecord?> GetLedgerBookAsync(Guid ledgerBookId, CancellationToken ct = default)
            => Task.FromResult(_books.GetValueOrDefault(ledgerBookId));

        public Task<IReadOnlyList<LedgerBookRecord>> ListLedgerBooksAsync(
            string? fundProfileId = null,
            Guid? fundStructureNodeId = null,
            FundStructureNodeKindDto? fundStructureNodeKind = null,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<LedgerBookRecord>>(_books.Values
                .Where(book => string.IsNullOrWhiteSpace(fundProfileId) || book.FundProfileId == fundProfileId)
                .ToArray());

        public Task<LedgerBookRecord> SaveLedgerBookAsync(LedgerBookRecord book, CancellationToken ct = default)
        {
            _books[book.LedgerBookId] = book;
            return Task.FromResult(book);
        }
    }
}
