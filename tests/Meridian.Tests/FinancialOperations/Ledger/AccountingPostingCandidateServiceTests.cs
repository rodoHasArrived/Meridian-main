using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Ledger;
using Meridian.Instruments.AssetOperations;
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
        var aggregateId = ledgerBookId;
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
                OrganizationId: "tenant-alpha",
                PortfolioId: "portfolio-income",
                BookId: ledgerBookId.ToString("D"),
                AccountId: "account-custodian-interest",
                CustomerId: "customer-custodian",
                VendorId: "vendor-bny",
                ProjectId: "project-interest-accrual",
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
        result.GeneratedPostingLines.Should().OnlyContain(line => line.Dimensions!.OrganizationId == "tenant-alpha");
        result.GeneratedPostingLines.Should().OnlyContain(line => line.Dimensions!.PortfolioId == "portfolio-income");
        result.GeneratedPostingLines.Should().OnlyContain(line => line.Dimensions!.BookId == ledgerBookId.ToString("D"));
        result.GeneratedPostingLines.Should().OnlyContain(line => line.Dimensions!.AccountId == "account-custodian-interest");
        result.GeneratedPostingLines.Should().OnlyContain(line => line.Dimensions!.CustomerId == "customer-custodian");
        result.GeneratedPostingLines.Should().OnlyContain(line => line.Dimensions!.VendorId == "vendor-bny");
        result.GeneratedPostingLines.Should().OnlyContain(line => line.Dimensions!.ProjectId == "project-interest-accrual");
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
            ledgerBookId,
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
            ledgerBookId,
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
        var aggregateId = Guid.NewGuid();

        var result = await service.BuildCandidateAsync(new PostingRuleJournalCandidateRequestDto(
            "fund-alpha",
            "CustodianInterestAccrual",
            125.44m,
            "USD",
            new DateOnly(2026, 5, 31),
            "controller@meridian.local",
            aggregateId,
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
            ledgerBookId,
            Guid.NewGuid(),
            DateTimeOffset.Parse("2026-05-31T21:00:00Z"),
            "Dimensional generated posting",
            AccountingBasis: AccountingBasisKindDto.Gaap,
            LedgerBookId: ledgerBookId,
            Dimensions: new LedgerDimensionSetDto(
                FundId: "fund-alpha",
                EntityId: "entity-master",
                InstrumentId: instrumentId,
                OrganizationId: "tenant-alpha",
                PortfolioId: "portfolio-income",
                BookId: ledgerBookId.ToString("D"),
                AccountId: "account-custodian-interest",
                CustomerId: "customer-custodian",
                VendorId: "vendor-bny",
                ProjectId: "project-interest-accrual",
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
        draftService.CapturedRequest.Lines.Should().OnlyContain(line => line.Dimensions!.OrganizationId == "tenant-alpha");
        draftService.CapturedRequest.Lines.Should().OnlyContain(line => line.Dimensions!.PortfolioId == "portfolio-income");
        draftService.CapturedRequest.Lines.Should().OnlyContain(line => line.Dimensions!.BookId == ledgerBookId.ToString("D"));
        draftService.CapturedRequest.Lines.Should().OnlyContain(line => line.Dimensions!.AccountId == "account-custodian-interest");
        draftService.CapturedRequest.Lines.Should().OnlyContain(line => line.Dimensions!.CustomerId == "customer-custodian");
        draftService.CapturedRequest.Lines.Should().OnlyContain(line => line.Dimensions!.VendorId == "vendor-bny");
        draftService.CapturedRequest.Lines.Should().OnlyContain(line => line.Dimensions!.ProjectId == "project-interest-accrual");
        draftService.CapturedRequest.Lines.Should().Contain(line =>
            line.Credit == 125.44m &&
            line.Dimensions!.CostCenterId == "income-review" &&
            line.Dimensions.ExternalGlDimensions["Department"] == "InvestmentOps");
    }

    [Fact]
    public async Task BuildCandidateAsync_AggregateOutsideLedgerBook_BlocksBeforeDraftWrite()
    {
        var ledgerBookId = Guid.NewGuid();
        var configurationService = await CreateSeededConfigurationServiceAsync(ledgerBookId: ledgerBookId);
        var draftService = new CapturingJournalDraftService();
        var service = new AccountingPostingCandidateService(configurationService, draftService);

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
            "Mismatched aggregate generated posting",
            AccountingBasis: AccountingBasisKindDto.Gaap,
            LedgerBookId: ledgerBookId,
            Dimensions: new LedgerDimensionSetDto(
                FundId: "fund-alpha",
                EntityId: "entity-master",
                BookId: ledgerBookId.ToString("D")),
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
            issue.Code == "posting-candidate.ledger-book-aggregate-required" &&
            issue.BlocksCandidate &&
            issue.TargetId == "aggregateId");
        draftService.CapturedRequest.Should().BeNull();
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
            ledgerBookId,
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
            ledgerBookId,
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
            ledgerBookId,
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
    public async Task BuildCandidateAsync_TypedContextMatchesAuthoritativeState_EchoesTypedLineage()
    {
        var harness = await CreateTypedCandidateHarnessAsync();

        var result = await harness.Service.BuildCandidateAsync(harness.Request);

        result.HasBlockingIssues.Should().BeFalse();
        result.BookContext.Should().Be(harness.Request.BookContext);
        result.BookPositionId.Should().Be(harness.Request.BookPositionId);
        result.EconomicEvent.Should().Be(harness.Request.EconomicEvent);
        result.ProjectionLineage.Should().Be(harness.Request.ProjectionLineage);
        result.RulePackReference.Should().Be(harness.Request.RulePackReference);
        result.GeneratedPostingLines.Should().OnlyContain(line =>
            line.Dimensions != null &&
            line.Dimensions.FundId == harness.Request.BookContext!.FundProfileId &&
            line.Dimensions.BookId == harness.Request.BookContext.LedgerBookId.ToString("D") &&
            line.Dimensions.InstrumentId == harness.Request.Dimensions!.InstrumentId &&
            line.Dimensions.PositionId == harness.Request.BookPositionId);
        result.PostingCommand.Should().NotBeNull();
        result.PostingCommand!.BookContext.Should().Be(harness.Request.BookContext);
        result.PostingCommand.BookPositionId.Should().Be(harness.Request.BookPositionId);
        result.PostingCommand.EconomicEvent.Should().Be(harness.Request.EconomicEvent);
        result.PostingCommand.ProjectionLineage.Should().Be(harness.Request.ProjectionLineage);
        result.PostingCommand.RulePackReference.Should().Be(harness.Request.RulePackReference);
    }

    [Fact]
    public async Task BuildCandidateAsync_TypedAssertionsMismatch_BlocksCandidateWithSpecificIssues()
    {
        var harness = await CreateTypedCandidateHarnessAsync();
        var wrongPositionId = Guid.NewGuid();
        var wrongEvent = harness.Request.EconomicEvent! with
        {
            EventId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid(),
            SecurityId = Guid.NewGuid(),
            BookPositionId = wrongPositionId
        };
        var request = harness.Request with
        {
            Dimensions = harness.Request.Dimensions! with { PositionId = wrongPositionId },
            BookContext = harness.Request.BookContext! with
            {
                BaseCurrency = "EUR",
                AccountingPolicyVersion = "v2",
                PeriodId = Guid.NewGuid()
            },
            EconomicEvent = wrongEvent,
            ProjectionLineage = harness.Request.ProjectionLineage! with { BookPositionId = wrongPositionId },
            RulePackReference = harness.Request.RulePackReference! with
            {
                RulePackVersion = "v2",
                SelectedRuleVersion = "v2"
            }
        };

        var result = await harness.Service.BuildCandidateAsync(request);

        result.HasBlockingIssues.Should().BeTrue();
        result.CanSubmitForApproval.Should().BeFalse();
        result.PostingCommand.Should().BeNull();
        result.Issues.Should().Contain(issue => issue.Code == "posting-candidate.book-context-currency-mismatch");
        result.Issues.Should().Contain(issue => issue.Code == "posting-candidate.book-context-authoritative-policy-mismatch");
        result.Issues.Should().Contain(issue => issue.Code == "posting-candidate.book-context-period-mismatch");
        result.Issues.Should().Contain(issue => issue.Code == "posting-candidate.economic-event-id-mismatch");
        result.Issues.Should().Contain(issue => issue.Code == "posting-candidate.economic-event-instrument-mismatch");
        result.Issues.Should().Contain(issue => issue.Code == "posting-candidate.projection-trigger-mismatch");
        result.Issues.Should().Contain(issue => issue.Code == "posting-candidate.book-position-dimension-mismatch");
        result.Issues.Should().Contain(issue => issue.Code == "posting-candidate.book-context-dimensions-mismatch");
        result.Issues.Should().Contain(issue => issue.Code == "posting-candidate.book-position-lineage-mismatch");
        result.Issues.Should().Contain(issue => issue.Code == "posting-candidate.rule-pack-selected-version-mismatch");
        result.Issues.Should().Contain(issue => issue.Code == "posting-candidate.rule-pack-version-mismatch");
        result.Issues.Should().Contain(issue => issue.Code == "posting-candidate.rule-pack-selected-rule-membership-mismatch");
    }

    [Fact]
    public async Task BuildCandidateAsync_TypedContextWithoutPeriod_ResolvesCandidatePeriodInAuthoritativeBook()
    {
        var harness = await CreateTypedCandidateHarnessAsync();
        var request = harness.Request with
        {
            BookContext = harness.Request.BookContext! with { PeriodId = null }
        };

        var result = await harness.Service.BuildCandidateAsync(request);

        result.HasBlockingIssues.Should().BeFalse();
        result.PostingCommand.Should().NotBeNull();
        result.Issues.Should().NotContain(issue =>
            issue.Code == "posting-candidate.book-context-period-not-found" ||
            issue.Code == "posting-candidate.book-context-period-ledger-book-mismatch");
    }

    [Fact]
    public async Task BuildCandidateAsync_TypedContextWithoutPeriod_MissingCandidatePeriodFailsClosed()
    {
        var harness = await CreateTypedCandidateHarnessAsync();
        var request = harness.Request with
        {
            PeriodId = Guid.NewGuid(),
            BookContext = harness.Request.BookContext! with { PeriodId = null }
        };

        var result = await harness.Service.BuildCandidateAsync(request);

        result.HasBlockingIssues.Should().BeTrue();
        result.PostingCommand.Should().BeNull();
        result.Issues.Should().Contain(issue =>
            issue.Code == "posting-candidate.book-context-period-not-found" &&
            issue.TargetId == "periodId" &&
            issue.BlocksCandidate);
    }

    [Fact]
    public async Task BuildCandidateAsync_CandidatePeriodOwnedByAnotherBookFailsClosed()
    {
        var harness = await CreateTypedCandidateHarnessAsync(
            authoritativePeriodLedgerBookId: Guid.NewGuid());
        var request = harness.Request with
        {
            BookContext = harness.Request.BookContext! with { PeriodId = null }
        };

        var result = await harness.Service.BuildCandidateAsync(request);

        result.HasBlockingIssues.Should().BeTrue();
        result.PostingCommand.Should().BeNull();
        result.Issues.Should().Contain(issue =>
            issue.Code == "posting-candidate.book-context-period-ledger-book-mismatch" &&
            issue.TargetId == "periodId" &&
            issue.BlocksCandidate);
    }

    [Fact]
    public async Task BuildCandidateAsync_GeneratedLineWithAnotherBookFailsClosed()
    {
        var harness = await CreateTypedCandidateHarnessAsync(
            generatedLineBookId: Guid.NewGuid());

        var result = await harness.Service.BuildCandidateAsync(harness.Request);

        result.HasBlockingIssues.Should().BeTrue();
        result.PostingCommand.Should().BeNull();
        result.Issues.Should().Contain(issue =>
            issue.Code == "posting-candidate.book-context-generated-line-book-mismatch" &&
            issue.TargetId.StartsWith("generatedPostingLines[", StringComparison.Ordinal) &&
            issue.BlocksCandidate);
    }

    [Fact]
    public async Task BuildCandidateAsync_GeneratedLineWithAnotherFundFailsClosed()
    {
        var harness = await CreateTypedCandidateHarnessAsync(
            generatedLineFundId: "fund-beta");

        var result = await harness.Service.BuildCandidateAsync(harness.Request);

        result.HasBlockingIssues.Should().BeTrue();
        result.PostingCommand.Should().BeNull();
        result.Issues.Should().Contain(issue =>
            issue.Code == "posting-candidate.book-context-generated-line-fund-mismatch" &&
            issue.TargetId.StartsWith("generatedPostingLines[", StringComparison.Ordinal) &&
            issue.BlocksCandidate);
    }

    [Fact]
    public async Task BuildCandidateAsync_BookPositionWithoutPositionDimensions_FailsClosed()
    {
        var harness = await CreateTypedCandidateHarnessAsync();
        var request = harness.Request with
        {
            Dimensions = harness.Request.Dimensions! with { PositionId = null },
            BookContext = harness.Request.BookContext! with
            {
                Dimensions = harness.Request.BookContext.Dimensions! with { PositionId = null }
            }
        };

        var result = await harness.Service.BuildCandidateAsync(request);

        result.HasBlockingIssues.Should().BeTrue();
        result.PostingCommand.Should().BeNull();
        result.Issues.Should().Contain(issue =>
            issue.Code == "posting-candidate.book-position-dimension-mismatch" && issue.BlocksCandidate);
        result.Issues.Should().Contain(issue =>
            issue.Code == "posting-candidate.book-position-generated-line-mismatch" && issue.BlocksCandidate);
    }

    [Fact]
    public async Task BuildCandidateAsync_TypedContextWithoutResolvers_FailsClosedAndEchoesAssertions()
    {
        var harness = await CreateTypedCandidateHarnessAsync();
        var service = new AccountingPostingCandidateService(
            harness.ConfigurationService,
            harness.DraftService);

        var result = await service.BuildCandidateAsync(harness.Request);

        result.HasBlockingIssues.Should().BeTrue();
        result.CanSubmitForApproval.Should().BeFalse();
        result.PostingCommand.Should().BeNull();
        result.BookContext.Should().Be(harness.Request.BookContext);
        result.RulePackReference.Should().Be(harness.Request.RulePackReference);
        result.Issues.Should().Contain(issue =>
            issue.Code == "posting-candidate.book-context-resolver-required" && issue.BlocksCandidate);
        result.Issues.Should().Contain(issue =>
            issue.Code == "posting-candidate.rule-pack-resolver-required" && issue.BlocksCandidate);
    }

    [Fact]
    public async Task BuildCandidateAsync_MbsFactorPaydown_RecalculatesPersistedProjectionBeforeDrafting()
    {
        var harness = await CreateAuthoritativeFactorHarnessAsync();

        var result = await harness.Service.BuildCandidateAsync(harness.Request);

        result.HasBlockingIssues.Should().BeFalse();
        result.PostingCommand.Should().NotBeNull();
        result.PostingCommand!.EconomicEvent!.EventId.Should().Be(harness.Request.SourceEventId!.Value);
        result.TotalDebits.Should().Be(1_750m);
        result.TotalCredits.Should().Be(1_750m);
    }

    [Fact]
    public async Task BuildCandidateAsync_MbsFactorPaydown_TamperedAmountFailsClosedAgainstServerCalculation()
    {
        var harness = await CreateAuthoritativeFactorHarnessAsync();

        var result = await harness.Service.BuildCandidateAsync(harness.Request with { EventAmount = 1m });

        result.HasBlockingIssues.Should().BeTrue();
        result.PostingCommand.Should().BeNull();
        result.Issues.Should().Contain(issue =>
            issue.Code == "posting-candidate.factor-paydown-amount-mismatch" && issue.BlocksCandidate);
        result.DryRunResult.GeneratedPostingLines.Should().OnlyContain(line => Math.Abs(line.Amount) == 1_750m);
    }

    [Fact]
    public async Task BuildCandidateAsync_MbsFactorPaydown_MissingHolderRoleFailsClosed()
    {
        var harness = await CreateAuthoritativeFactorHarnessAsync();
        harness.AssetOperations.Detail = harness.AssetOperations.Detail! with { InstrumentRoles = [] };

        var result = await harness.Service.BuildCandidateAsync(harness.Request);

        result.HasBlockingIssues.Should().BeTrue();
        result.Issues.Should().Contain(issue =>
            issue.Code == "posting-candidate.instrument-holder-role-required" && issue.BlocksCandidate);
    }

    [Fact]
    public async Task BuildCandidateAsync_MbsFactorPaydown_OmittedLineageCannotBypassAuthoritativeRecalculation()
    {
        var harness = await CreateAuthoritativeFactorHarnessAsync();

        var result = await harness.Service.BuildCandidateAsync(harness.Request with
        {
            EventAmount = 1m,
            ProjectionLineage = null
        });

        result.HasBlockingIssues.Should().BeTrue();
        result.PostingCommand.Should().BeNull();
        result.Issues.Should().Contain(issue =>
            issue.Code == "posting-candidate.instrument-factor-lineage-required" && issue.BlocksCandidate);
    }

    [Fact]
    public async Task BuildCandidateAsync_MbsFactorPaydown_ClientSelectedModelCannotBypassAuthoritativeRecalculation()
    {
        var harness = await CreateAuthoritativeFactorHarnessAsync();

        var result = await harness.Service.BuildCandidateAsync(harness.Request with
        {
            EventAmount = 1m,
            ProjectionLineage = harness.Request.ProjectionLineage! with { ModelKey = "client-selected-model" }
        });

        result.HasBlockingIssues.Should().BeTrue();
        result.PostingCommand.Should().BeNull();
        result.Issues.Should().Contain(issue =>
            issue.Code == "posting-candidate.instrument-factor-lineage-required" && issue.BlocksCandidate);
    }

    [Fact]
    public async Task BuildCandidateAsync_MbsFactorPaydown_MissingRulePackReferenceFailsClosed()
    {
        var harness = await CreateAuthoritativeFactorHarnessAsync();

        var result = await harness.Service.BuildCandidateAsync(harness.Request with
        {
            RulePackReference = null
        });

        result.HasBlockingIssues.Should().BeTrue();
        result.PostingCommand.Should().BeNull();
        result.Issues.Should().Contain(issue =>
            issue.Code == "posting-candidate.rule-pack-reference-required" && issue.BlocksCandidate);
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
            EvidenceLinks: [ApprovalEvidence("fund-alpha", ledgerBookId, sourceEventId)]));

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
        appended.Entry.Lines.Should().OnlyContain(line => line.Dimensions != null);
        appended.Entry.Lines.Should().OnlyContain(line =>
            line.Dimensions!.FundId == "fund-alpha" &&
            line.Dimensions.EntityId == "entity-master" &&
            line.Dimensions.OrganizationId == "tenant-alpha" &&
            line.Dimensions.PortfolioId == "portfolio-income" &&
            line.Dimensions.BookId == ledgerBookId.ToString("D") &&
            line.Dimensions.AccountId == "account-custodian-interest" &&
            line.Dimensions.CustomerId == "customer-custodian" &&
            line.Dimensions.VendorId == "vendor-bny" &&
            line.Dimensions.ProjectId == "project-interest-accrual");
    }

    [Fact]
    public void NormalizeAndValidate_TypedInstrumentCommand_StampsDurableJournalProofMetadata()
    {
        var bookId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var positionId = Guid.NewGuid();
        var securityId = Guid.NewGuid();
        var commandId = Guid.NewGuid();
        var journalId = Guid.NewGuid();
        var timestamp = DateTimeOffset.Parse("2026-05-31T12:00:00Z");
        var economicEvent = new EconomicEventReferenceDto(
            eventId,
            FactorPaydownProjectionService.EventType,
            1,
            new DateOnly(2026, 5, 31),
            timestamp,
            "SecurityMaster",
            "factor-row-2026-05",
            SourceContentHash: "sha256:factor-row",
            EvidenceLinks: ["evidence://factor/2026-05"])
        {
            SecurityId = securityId,
            BookPositionId = positionId
        };
        var lineage = new ProjectionLineageDto(
            Guid.NewGuid(),
            Guid.NewGuid(),
            FactorPaydownProjectionService.ModelKey,
            FactorPaydownProjectionService.ModelVersion,
            FactorPaydownProjectionService.EngineVersion,
            "Base",
            economicEvent.EffectiveDate,
            timestamp,
            "AssetOperations",
            positionId.ToString("D"),
            economicEvent)
        {
            BookPositionId = positionId
        };
        var command = new AccountingPostingCommandDto(
            commandId,
            bookId,
            periodId,
            economicEvent.EffectiveDate,
            timestamp,
            $"{bookId:N}:{eventId:N}",
            SourceEventId: eventId,
            CorrelationId: Guid.NewGuid(),
            CausationId: eventId,
            SourceEventType: economicEvent.EventType,
            ApprovalState: AccountingPostingApprovalStateDto.Approved,
            ApprovalId: "approval-mbs-2026-05",
            Evidence:
            [
                new AccountingPostingEvidenceReferenceDto(
                    "factor-evidence-2026-05",
                    "evidence://factor/2026-05",
                    AccountingPostingEvidenceKindDto.Source,
                    "SecurityMaster",
                    timestamp,
                    "controller@meridian.local")
            ],
            LedgerBookId: bookId)
        {
            BookPositionId = positionId,
            EconomicEvent = economicEvent,
            ProjectionLineage = lineage,
            BookContext = new AccountingBookContextDto(
                bookId,
                "fund-alpha",
                Guid.NewGuid(),
                FundStructureNodeKindDto.Fund,
                "MBS GAAP Book",
                "USD",
                AccountingBasisKindDto.Gaap,
                "gaap-mbs-v1",
                "v1",
                periodId),
            RulePackReference = new AccountingRulePackReferenceDto(
                "gaap-mbs-rules",
                "v1",
                "posting.mbs-factor-paydown",
                "v1")
        };
        var entry = new JournalEntry(
            journalId,
            timestamp,
            "MBS principal paydown",
            [
                new LedgerEntry(Guid.NewGuid(), journalId, timestamp, LedgerAccounts.Cash, 1_750m, 0m, "MBS principal paydown"),
                new LedgerEntry(Guid.NewGuid(), journalId, timestamp, LedgerAccounts.Securities("FNPOOL1"), 0m, 1_750m, "MBS principal paydown")
            ],
            new JournalEntryMetadata());
        var write = new LedgerJournalEntryWrite(
            entry,
            bookId,
            periodId,
            commandId,
            command.CorrelationId,
            AccountingBasisKindDto.Gaap,
            "gaap-mbs-v1",
            "v1",
            "posting.mbs-factor-paydown",
            "v1",
            eventId,
            PostingCommand: command,
            LedgerBookId: bookId);

        var normalized = AccountingPostingCommandValidator.NormalizeAndValidate(write);

        normalized.Entry.Metadata.SecurityId.Should().Be(securityId);
        normalized.Entry.Metadata.Tags.Should().Contain(new Dictionary<string, string>
        {
            ["postingCommandId"] = commandId.ToString("D"),
            ["approvalId"] = "approval-mbs-2026-05",
            ["approvalState"] = "Approved",
            ["sourceEventId"] = eventId.ToString("D"),
            ["securityId"] = securityId.ToString("D"),
            ["bookPositionId"] = positionId.ToString("D"),
            ["projectionRunId"] = lineage.ProjectionRunId.ToString("D"),
            ["projectionModelKey"] = FactorPaydownProjectionService.ModelKey,
            ["rulePackId"] = "gaap-mbs-rules",
            ["selectedRuleId"] = "posting.mbs-factor-paydown"
        });
        normalized.Entry.Lines.Should().OnlyContain(line =>
            line.Dimensions != null &&
            line.Dimensions.InstrumentId == securityId &&
            line.Dimensions.PositionId == positionId);
        normalized.Entry.IsBalanced.Should().BeTrue();

        var firstLine = entry.Lines[0];
        var conflictingEntry = new JournalEntry(
            entry.JournalEntryId,
            entry.Timestamp,
            entry.Description,
            [
                new LedgerEntry(
                    firstLine.EntryId,
                    firstLine.JournalEntryId,
                    firstLine.Timestamp,
                    firstLine.Account,
                    firstLine.Debit,
                    firstLine.Credit,
                    firstLine.Description,
                    new LedgerLineDimensionSet(InstrumentId: Guid.NewGuid()) { PositionId = positionId }),
                entry.Lines[1]
            ],
            entry.Metadata);

        var act = () => AccountingPostingCommandValidator.NormalizeAndValidate(write with { Entry = conflictingEntry });

        act.Should().Throw<LedgerValidationException>()
            .WithMessage("*instrument dimension conflicts*");
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
            EvidenceLinks: [ApprovalEvidence("fund-alpha", gaapLedgerBookId, sourceEventId)]));
        var cash = await service.PostCandidateAsync(new PostPostingRuleJournalCandidateRequestDto(
            BuildCandidateRequest(
                cashLedgerBookId,
                cashPeriodId,
                sourceEventId,
                AccountingBasisKindDto.Cash,
                "cash-accrual-v1"),
            "reviewer@meridian.local",
            "approval-cash-interest-202605",
            EvidenceLinks: [ApprovalEvidence("fund-alpha", cashLedgerBookId, sourceEventId)]));

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
            EvidenceLinks: [ApprovalEvidence("fund-alpha", ledgerBookId, sourceEventId)]);

        var first = await service.PostCandidateAsync(request);
        var second = await service.PostCandidateAsync(request);

        second.WasReplay.Should().BeTrue();
        second.PostedJournal.JournalEntryId.Should().Be(first.PostedJournal.JournalEntryId);
        second.PostedJournal.SourceEventId.Should().Be(sourceEventId);
        store.Appended.Should().ContainSingle();
    }

    [Fact]
    public async Task PostCandidateAsync_DifferentEconomicsUnderRetainedSourceEventFailsClosed()
    {
        var ledgerBookId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var sourceEventId = Guid.NewGuid();
        var candidateService = await CreateSeededCandidateServiceAsync(ledgerBookId: ledgerBookId);
        var store = new RecordingLedgerJournalStore(
            BuildLedgerBook(ledgerBookId, AccountingBasisKindDto.Gaap),
            BuildPeriod(periodId, ledgerBookId));
        var service = new AccountingPostingCandidatePostService(candidateService, store);

        var firstCandidate = BuildCandidateRequest(
            ledgerBookId,
            periodId,
            sourceEventId,
            AccountingBasisKindDto.Gaap,
            "gaap-accrual-v1");
        var first = await service.PostCandidateAsync(new PostPostingRuleJournalCandidateRequestDto(
            firstCandidate,
            "reviewer@meridian.local",
            "approval-generated-interest-202605",
            EvidenceLinks: [ApprovalEvidence("fund-alpha", ledgerBookId, sourceEventId)]));

        // Same (book, source event); materially different economics. The pair is uniquely
        // indexed, so this posting can never be appended — it must not be reported as posted.
        var act = () => service.PostCandidateAsync(new PostPostingRuleJournalCandidateRequestDto(
            firstCandidate with
            {
                EventAmount = 999_999.00m,
                Description = "Completely different posting under a reused source event"
            },
            "reviewer@meridian.local",
            "approval-generated-interest-202605",
            EvidenceLinks: [ApprovalEvidence("fund-alpha", ledgerBookId, sourceEventId)]));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already retains journal*different accounting content*");
        store.Appended.Should().ContainSingle();
        store.Appended[0].Entry.Lines.Sum(line => line.Debit).Should().Be(125.44m);
        first.WasReplay.Should().BeFalse();
    }

    [Fact]
    public async Task PostCandidateAsync_ReplayedNonTreasurySourceEventReturnsExistingJournal()
    {
        // Without a treasury context the drafted metadata carries no idempotency key; the append
        // path stamps the posting command's key into the retained journal. A replay must still be
        // recognised as one rather than conflicting on a key the rebuild has not been given yet.
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
                "gaap-accrual-v1") with
            {
                TreasuryContext = null
            },
            "reviewer@meridian.local",
            "approval-generated-interest-202605",
            EvidenceLinks: [ApprovalEvidence("fund-alpha", ledgerBookId, sourceEventId)]);

        var first = await service.PostCandidateAsync(request);
        var second = await service.PostCandidateAsync(request);

        second.WasReplay.Should().BeTrue();
        second.PostedJournal.JournalEntryId.Should().Be(first.PostedJournal.JournalEntryId);
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
            "approval-generated-interest-202605",
            EvidenceLinks: [ApprovalEvidence("fund-alpha", ledgerBookId, sourceEventId)]));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*aggregate id must equal the target ledger book id*");
        store.Appended.Should().BeEmpty();
    }

    [Fact]
    public async Task PostCandidateAsync_JournalMetadataLedgerBookMismatchBlocksAppend()
    {
        var ledgerBookId = Guid.NewGuid();
        var wrongLedgerBookId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var sourceEventId = Guid.NewGuid();
        var request = BuildCandidateRequest(
            ledgerBookId,
            periodId,
            sourceEventId,
            AccountingBasisKindDto.Gaap,
            "gaap-accrual-v1");
        var builder = new FixedCandidateWriteBuilder(BuildCandidateWrite(
            request,
            journalMetadataLedgerBookId: wrongLedgerBookId,
            lineDimensionBookId: ledgerBookId));
        var store = new RecordingLedgerJournalStore(
            BuildLedgerBook(ledgerBookId, AccountingBasisKindDto.Gaap),
            BuildPeriod(periodId, ledgerBookId));
        var service = new AccountingPostingCandidatePostService(builder, store);

        var act = () => service.PostCandidateAsync(new PostPostingRuleJournalCandidateRequestDto(
            request,
            "reviewer@meridian.local",
            "approval-generated-interest-202605",
            EvidenceLinks: [ApprovalEvidence("fund-alpha", ledgerBookId, sourceEventId)]));

        await act.Should().ThrowAsync<LedgerValidationException>()
            .WithMessage("*journal metadata ledger book*does not match approved ledger book*");
        store.Appended.Should().BeEmpty();
    }

    [Fact]
    public async Task PostCandidateAsync_LineDimensionLedgerBookMismatchBlocksAppend()
    {
        var ledgerBookId = Guid.NewGuid();
        var wrongLedgerBookId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var sourceEventId = Guid.NewGuid();
        var request = BuildCandidateRequest(
            ledgerBookId,
            periodId,
            sourceEventId,
            AccountingBasisKindDto.Gaap,
            "gaap-accrual-v1");
        var builder = new FixedCandidateWriteBuilder(BuildCandidateWrite(
            request,
            journalMetadataLedgerBookId: ledgerBookId,
            lineDimensionBookId: wrongLedgerBookId));
        var store = new RecordingLedgerJournalStore(
            BuildLedgerBook(ledgerBookId, AccountingBasisKindDto.Gaap),
            BuildPeriod(periodId, ledgerBookId));
        var service = new AccountingPostingCandidatePostService(builder, store);

        var act = () => service.PostCandidateAsync(new PostPostingRuleJournalCandidateRequestDto(
            request,
            "reviewer@meridian.local",
            "approval-generated-interest-202605",
            EvidenceLinks: [ApprovalEvidence("fund-alpha", ledgerBookId, sourceEventId)]));

        await act.Should().ThrowAsync<LedgerValidationException>()
            .WithMessage("*dimension book*does not match approved ledger book*");
        store.Appended.Should().BeEmpty();
    }

    [Theory]
    [InlineData(AccountingPostingApprovalStateDto.Approved)]
    [InlineData(AccountingPostingApprovalStateDto.NotRequired)]
    [InlineData(AccountingPostingApprovalStateDto.Rejected)]
    public async Task PostCandidateAsync_NonPendingCandidateCommandBlocksAppend(
        AccountingPostingApprovalStateDto approvalState)
    {
        var ledgerBookId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var sourceEventId = Guid.NewGuid();
        var request = BuildCandidateRequest(
            ledgerBookId,
            periodId,
            sourceEventId,
            AccountingBasisKindDto.Gaap,
            "gaap-accrual-v1");
        var builder = new FixedCandidateWriteBuilder(BuildCandidateWrite(
            request,
            journalMetadataLedgerBookId: ledgerBookId,
            lineDimensionBookId: ledgerBookId,
            approvalState: approvalState));
        var store = new RecordingLedgerJournalStore(
            BuildLedgerBook(ledgerBookId, AccountingBasisKindDto.Gaap),
            BuildPeriod(periodId, ledgerBookId));
        var service = new AccountingPostingCandidatePostService(builder, store);

        var act = () => service.PostCandidateAsync(new PostPostingRuleJournalCandidateRequestDto(
            request,
            "reviewer@meridian.local",
            "approval-generated-interest-202605",
            EvidenceLinks: [ApprovalEvidence("fund-alpha", ledgerBookId, sourceEventId)]));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*pending approval command*'{approvalState}'*");
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
            EvidenceLinks: [ApprovalEvidence("fund-alpha", ledgerBookId, sourceEventId)]));

        await act.Should().ThrowAsync<LedgerValidationException>()
            .WithMessage("*hard-closed; no postings are permitted*");
        store.Appended.Should().BeEmpty();
    }

    [Fact]
    public async Task PostCandidateAsync_GenericApprovalEvidenceBlocksAppend()
    {
        var ledgerBookId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var sourceEventId = Guid.NewGuid();
        var candidateService = await CreateSeededCandidateServiceAsync(ledgerBookId: ledgerBookId);
        var store = new RecordingLedgerJournalStore(
            BuildLedgerBook(ledgerBookId, AccountingBasisKindDto.Gaap),
            BuildPeriod(periodId, ledgerBookId));
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

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*approval evidence must name approval intent, fund 'fund-alpha'*ledger book*source event*same retained artifact*");
        store.Appended.Should().BeEmpty();
    }

    [Fact]
    public async Task PostCandidateAsync_ExtendedLedgerBookEvidenceTokenBlocksAppend()
    {
        var ledgerBookId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var sourceEventId = Guid.NewGuid();
        var candidateService = await CreateSeededCandidateServiceAsync(ledgerBookId: ledgerBookId);
        var store = new RecordingLedgerJournalStore(
            BuildLedgerBook(ledgerBookId, AccountingBasisKindDto.Gaap),
            BuildPeriod(periodId, ledgerBookId));
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
            EvidenceLinks: [$"approval://workpaper/fund-alpha/ledger-book:{ledgerBookId:D}ffff/source-event:{sourceEventId:D}/reviewed"]));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*approval evidence must name approval intent, fund 'fund-alpha'*ledger book*source event*same retained artifact*");
        store.Appended.Should().BeEmpty();
    }

    [Fact]
    public async Task PostCandidateAsync_TenantScopedCandidateRequiresTenantCompanyApprovalEvidence()
    {
        var ledgerBookId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var sourceEventId = Guid.NewGuid();
        var candidateService = await CreateSeededCandidateServiceAsync(ledgerBookId: ledgerBookId);
        var store = new RecordingLedgerJournalStore(
            BuildLedgerBook(ledgerBookId, AccountingBasisKindDto.Gaap),
            BuildPeriod(periodId, ledgerBookId));
        var service = new AccountingPostingCandidatePostService(candidateService, store);
        var tenantScopedCandidate = BuildCandidateRequest(
            ledgerBookId,
            periodId,
            sourceEventId,
            AccountingBasisKindDto.Gaap,
            "gaap-accrual-v1") with
        {
            TenantId = "tenant-alpha",
            CompanyId = "company-alpha"
        };

        var act = () => service.PostCandidateAsync(new PostPostingRuleJournalCandidateRequestDto(
            tenantScopedCandidate,
            "reviewer@meridian.local",
            "approval-generated-interest-202605",
            EvidenceLinks: [ApprovalEvidence("fund-alpha", ledgerBookId, sourceEventId)]));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*tenant 'tenant-alpha', company 'company-alpha'*same retained artifact*");
        store.Appended.Should().BeEmpty();
    }

    [Fact]
    public async Task PostCandidateAsync_SameActorAsCandidatePreparerBlocksAppend()
    {
        var ledgerBookId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var sourceEventId = Guid.NewGuid();
        var candidateService = await CreateSeededCandidateServiceAsync(ledgerBookId: ledgerBookId);
        var store = new RecordingLedgerJournalStore(
            BuildLedgerBook(ledgerBookId, AccountingBasisKindDto.Gaap),
            BuildPeriod(periodId, ledgerBookId));
        var service = new AccountingPostingCandidatePostService(candidateService, store);

        var act = () => service.PostCandidateAsync(new PostPostingRuleJournalCandidateRequestDto(
            BuildCandidateRequest(
                ledgerBookId,
                periodId,
                sourceEventId,
                AccountingBasisKindDto.Gaap,
                "gaap-accrual-v1"),
            "controller@meridian.local",
            "approval-generated-interest-202605",
            EvidenceLinks: [ApprovalEvidence("fund-alpha", ledgerBookId, sourceEventId)]));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*approval by an operator independent from the candidate preparer*");
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

    private static async Task<TypedCandidateHarness> CreateTypedCandidateHarnessAsync(
        string? generatedLineFundId = null,
        Guid? generatedLineBookId = null,
        Guid? authoritativePeriodLedgerBookId = null)
    {
        var ledgerBookId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var ownerNodeId = Guid.NewGuid();
        var securityId = Guid.NewGuid();
        var positionId = Guid.NewGuid();
        var sourceEventId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        const string evidenceLink = "provider://custodian/interest-accruals/2026-05";
        var effectiveDate = new DateOnly(2026, 5, 31);
        var occurredAt = DateTimeOffset.Parse("2026-05-31T20:00:00Z");
        var generatedAt = DateTimeOffset.Parse("2026-05-31T20:05:00Z");

        var generatedLineDimensions = generatedLineFundId is null && !generatedLineBookId.HasValue
            ? null
            : new LedgerDimensionSetDto(
                FundId: generatedLineFundId,
                BookId: generatedLineBookId?.ToString("D"));
        var configurationService = await CreateSeededConfigurationServiceAsync(
            ledgerBookId: ledgerBookId,
            generatedLineDimensions: generatedLineDimensions);
        var policyService = new AccountingPolicyService();
        await policyService.CreatePolicyAsync(new CreateAccountingPolicyRequest(
            AccountingBasisKindDto.Gaap,
            PolicyId: "gaap-accrual-v1",
            Version: "v1",
            DisplayName: "GAAP governed posting-rule alignment",
            EffectiveFrom: new DateOnly(2026, 1, 1),
            RulePack: new AccountingPolicyRulePackDto(
                "gaap-accrual-rules",
                "v1",
                [
                    new AccountingPolicyRuleDto(
                        "posting.interest-accrual",
                        AccountingTreatmentKindDto.Accrual,
                        RuleVersion: "v1",
                        SourceEventType: "CustodianInterestAccrual",
                        RequiresEvidence: true,
                        RequiresApproval: true,
                        AllowsAutoPosting: false)
                ])));
        var draftService = new AccountingJournalDraftService(
            policyService,
            new AccountingBasisProjectionService(policyService));
        var ledgerBook = new LedgerBookDto(
            ledgerBookId,
            "fund-alpha",
            ownerNodeId,
            FundStructureNodeKindDto.Fund,
            "GAAP operating ledger",
            "USD",
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            AccountingBasis: AccountingBasisKindDto.Gaap,
            AccountingPolicyId: "gaap-accrual-v1",
            AccountingPolicyVersion: "v1");
        var period = new LedgerPeriodDto(
            periodId,
            ledgerBookId,
            2026,
            5,
            "May 2026",
            new DateOnly(2026, 5, 1),
            effectiveDate,
            LedgerPeriodStatusDto.Open,
            DateTimeOffset.Parse("2026-05-01T00:00:00Z"),
            ClosedAt: null,
            Version: 1,
            AccountingBasis: AccountingBasisKindDto.Gaap,
            AccountingPolicyId: "gaap-accrual-v1",
            AccountingPolicyVersion: "v1");
        var dimensions = new LedgerDimensionSetDto(
            FundId: "fund-alpha",
            EntityId: "entity-master",
            InstrumentId: securityId,
            OrganizationId: "tenant-alpha",
            PortfolioId: "portfolio-income",
            BookId: ledgerBookId.ToString("D"),
            AccountId: "account-custodian-interest",
            CustomerId: "customer-custodian",
            VendorId: "vendor-bny",
            ProjectId: "project-interest-accrual")
        {
            PositionId = positionId
        };
        var bookContext = new AccountingBookContextDto(
            ledgerBookId,
            "fund-alpha",
            ownerNodeId,
            FundStructureNodeKindDto.Fund,
            ledgerBook.DisplayName,
            "USD",
            AccountingBasisKindDto.Gaap,
            "gaap-accrual-v1",
            "v1",
            periodId,
            dimensions);
        var economicEvent = new EconomicEventReferenceDto(
            sourceEventId,
            "CustodianInterestAccrual",
            EventVersion: 1,
            effectiveDate,
            occurredAt,
            SourceDomain: "SecurityMaster",
            SourceEntityId: securityId.ToString("D"),
            CorrelationId: correlationId,
            SourceContentHash: "sha256:factor-evidence",
            EvidenceLinks: [evidenceLink])
        {
            SecurityId = securityId,
            BookPositionId = positionId
        };
        var lineage = new ProjectionLineageDto(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "typed-contract-test-projection",
            "v1",
            "v1",
            "Actual",
            effectiveDate,
            generatedAt,
            "AssetOperations",
            positionId.ToString("D"),
            economicEvent,
            EvidenceLinks: [evidenceLink])
        {
            BookPositionId = positionId
        };
        var request = BuildCandidateRequest(
            ledgerBookId,
            periodId,
            sourceEventId,
            AccountingBasisKindDto.Gaap,
            "gaap-accrual-v1") with
        {
            CorrelationId = correlationId,
            Dimensions = dimensions,
            BookContext = bookContext,
            BookPositionId = positionId,
            EconomicEvent = economicEvent,
            ProjectionLineage = lineage,
            RulePackReference = new AccountingRulePackReferenceDto(
                "gaap-accrual-rules",
                "v1",
                "posting.interest-accrual",
                "v1")
        };
        var service = new AccountingPostingCandidateService(
            configurationService,
            draftService,
            new StaticLedgerBookService(
                ledgerBook,
                authoritativePeriodLedgerBookId.HasValue
                    ? period with { LedgerBookId = authoritativePeriodLedgerBookId.Value }
                    : period),
            policyService);

        return new TypedCandidateHarness(service, configurationService, draftService, request);
    }

    private static async Task<AuthoritativeFactorHarness> CreateAuthoritativeFactorHarnessAsync()
    {
        var ledgerBookId = Guid.Parse("c1000000-0000-4000-8000-000000000001");
        var periodId = Guid.Parse("c1000000-0000-4000-8000-000000000002");
        var ownerNodeId = Guid.Parse("c1000000-0000-4000-8000-000000000003");
        var securityId = Guid.Parse("c1000000-0000-4000-8000-000000000004");
        var roleId = Guid.Parse("c1000000-0000-4000-8000-000000000005");
        var positionId = Guid.Parse("c1000000-0000-4000-8000-000000000006");
        var correlationId = Guid.Parse("c1000000-0000-4000-8000-000000000007");
        var effectiveDate = new DateOnly(2026, 5, 31);
        const string evidence = "evidence://factor/mbs-2026-05";
        var dimensions = new LedgerDimensionSetDto(
            "fund-alpha",
            "entity-master",
            InstrumentId: securityId,
            BookId: ledgerBookId.ToString("D"))
        {
            PositionId = positionId
        };
        var bookContext = new AccountingBookContextDto(
            ledgerBookId,
            "fund-alpha",
            ownerNodeId,
            FundStructureNodeKindDto.Fund,
            "Fund Alpha GAAP",
            "USD",
            AccountingBasisKindDto.Gaap,
            "gaap-mbs-v1",
            "v1",
            periodId,
            dimensions);
        var projector = new FactorPaydownProjectionService();
        var projection = projector.Project(new FactorPaydownProjectionRequest(
            securityId,
            positionId,
            4,
            4,
            100_000m,
            0.9800m,
            0.9625m,
            "USD",
            effectiveDate,
            DateTimeOffset.Parse("2026-05-31T00:00:00Z"),
            "SecurityMaster",
            "mbs-factor-row-2026-05",
            "sha256:mbs-factor-row-2026-05",
            [evidence],
            correlationId));
        var role = new InstrumentRoleDto(
            roleId,
            securityId,
            "fund-alpha",
            "Fund",
            InstrumentRoleKinds.Holder,
            InstrumentAccountingSides.Debit,
            InstrumentEconomicSides.Asset,
            new DateOnly(2026, 1, 1),
            Version: 2,
            EvidenceLinks: [evidence]);
        var position = new BookPositionDto(
            positionId,
            securityId,
            roleId,
            bookContext,
            BookPositionSides.Long,
            "Active",
            new DateOnly(2026, 1, 1),
            Version: 4,
            CurrentEconomicState: projection.EconomicState,
            ProjectionLineage: projection.Lineage,
            EvidenceLinks: [evidence]);
        var subject = new AssetOperationSubjectDto(
            securityId,
            "MortgageBackedSecurity",
            "Agency MBS Pool",
            "FNPOOL1",
            ["FactorProcessing"]);
        var readiness = new AssetOperationsReadinessDto(
            securityId,
            "Ready",
            [],
            [],
            [],
            [],
            DateTimeOffset.Parse("2026-05-31T01:00:00Z"),
            "AssetOperations",
            positionId.ToString("D"));
        var detail = new AssetOperationsDetailDto(subject, [], [], [], [], [], [], [], [], readiness, [])
        {
            InstrumentRoles = [role],
            BookPositions = [position],
            PositionEconomicStates = [projection.EconomicState!],
            ProjectionLineages = [projection.Lineage!]
        };

        var configurationService = new AccountingConfigurationService(
            new InMemoryAccountingConfigurationStore(),
            new InMemoryAccountingActionAuditStore());
        await SeedCandidateConfigurationAsync(
            configurationService,
            ledgerBookId,
            ruleId: "posting.mbs-factor-paydown",
            generatedLineDimensions: dimensions,
            sourceEventType: FactorPaydownProjectionService.EventType);
        var policyService = new AccountingPolicyService();
        await policyService.CreatePolicyAsync(new CreateAccountingPolicyRequest(
            AccountingBasisKindDto.Gaap,
            "gaap-mbs-v1",
            "v1",
            "GAAP MBS factor treatment",
            new DateOnly(2026, 1, 1),
            RulePack: new AccountingPolicyRulePackDto(
                "gaap-mbs-rules",
                "v1",
                [
                    new AccountingPolicyRuleDto(
                        "posting.mbs-factor-paydown",
                        AccountingTreatmentKindDto.Amortization,
                        "v1",
                        FactorPaydownProjectionService.EventType,
                        RequiresEvidence: true,
                        RequiresApproval: true,
                        AllowsAutoPosting: false)
                ])));
        var ledgerBook = new LedgerBookDto(
            ledgerBookId,
            "fund-alpha",
            ownerNodeId,
            FundStructureNodeKindDto.Fund,
            "Fund Alpha GAAP",
            "USD",
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            AccountingBasis: AccountingBasisKindDto.Gaap,
            AccountingPolicyId: "gaap-mbs-v1",
            AccountingPolicyVersion: "v1");
        var period = new LedgerPeriodDto(
            periodId,
            ledgerBookId,
            2026,
            5,
            "May 2026",
            new DateOnly(2026, 5, 1),
            effectiveDate,
            LedgerPeriodStatusDto.Open,
            DateTimeOffset.Parse("2026-05-01T00:00:00Z"),
            null,
            1,
            AccountingBasisKindDto.Gaap,
            "gaap-mbs-v1",
            "v1");
        var request = new PostingRuleJournalCandidateRequestDto(
            "fund-alpha",
            FactorPaydownProjectionService.EventType,
            1_750m,
            "USD",
            effectiveDate,
            "preparer@meridian.local",
            ledgerBookId,
            periodId,
            DateTimeOffset.Parse("2026-05-31T12:00:00Z"),
            "MBS factor principal paydown",
            AccountingBasisKindDto.Gaap,
            ledgerBookId,
            dimensions,
            InstrumentSymbol: "FNPOOL1",
            CorrelationId: correlationId,
            SourceEventId: projection.EconomicEvent!.EventId,
            PolicyId: "gaap-mbs-v1",
            TreatmentKind: AccountingTreatmentKindDto.Amortization,
            EvidenceLinks: [evidence])
        {
            BookContext = bookContext,
            BookPositionId = positionId,
            EconomicEvent = projection.EconomicEvent,
            ProjectionLineage = projection.Lineage,
            RulePackReference = new AccountingRulePackReferenceDto(
                "gaap-mbs-rules",
                "v1",
                "posting.mbs-factor-paydown",
                "v1")
        };
        var assetOperations = new StaticAssetOperationsQueryService { Detail = detail };
        var draftService = new AccountingJournalDraftService(
            policyService,
            new AccountingBasisProjectionService(policyService));
        var service = new AccountingPostingCandidateService(
            configurationService,
            draftService,
            new StaticLedgerBookService(ledgerBook, period),
            policyService,
            assetOperations,
            projector);
        return new AuthoritativeFactorHarness(service, request, assetOperations);
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
        Guid? ledgerBookId = null,
        LedgerDimensionSetDto? generatedLineDimensions = null)
    {
        var configurationService = new AccountingConfigurationService(
            new InMemoryAccountingConfigurationStore(),
            new InMemoryAccountingActionAuditStore());

        await SeedCandidateConfigurationAsync(
            configurationService,
            ledgerBookId,
            incomeAccountType,
            creditAmount,
            generatedLineDimensions: generatedLineDimensions);

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
        string costCenterId = "income-review",
        LedgerDimensionSetDto? generatedLineDimensions = null,
        string sourceEventType = "CustodianInterestAccrual")
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
                sourceEventType,
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
                        generatedLineDimensions ?? new LedgerDimensionSetDto(CostCenterId: costCenterId),
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
            Dimensions: new LedgerDimensionSetDto(
                FundId: "fund-alpha",
                EntityId: "entity-master",
                OrganizationId: "tenant-alpha",
                PortfolioId: "portfolio-income",
                BookId: ledgerBookId.ToString("D"),
                AccountId: "account-custodian-interest",
                CustomerId: "customer-custodian",
                VendorId: "vendor-bny",
                ProjectId: "project-interest-accrual"),
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

    private static string ApprovalEvidence(
        string fundProfileId,
        Guid ledgerBookId,
        Guid sourceEventId,
        string? tenantId = null,
        string? companyId = null)
    {
        var tenantSegment = string.IsNullOrWhiteSpace(tenantId) ? string.Empty : $"/tenant/{tenantId.Trim()}";
        var companySegment = string.IsNullOrWhiteSpace(companyId) ? string.Empty : $"/company/{companyId.Trim()}";
        return $"approval://workpaper/{fundProfileId}{tenantSegment}{companySegment}/ledger-book:{ledgerBookId:D}/source-event:{sourceEventId:D}/reviewed";
    }

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

    private static AccountingPostingCandidateWriteResult BuildCandidateWrite(
        PostingRuleJournalCandidateRequestDto request,
        Guid journalMetadataLedgerBookId,
        Guid lineDimensionBookId,
        AccountingPostingApprovalStateDto approvalState = AccountingPostingApprovalStateDto.Pending)
    {
        var journalEntryId = Guid.NewGuid();
        var lineDimensions = new LedgerLineDimensionSet(
            FundId: "fund-alpha",
            EntityId: "entity-master",
            BookId: lineDimensionBookId.ToString("D"));
        var entry = new JournalEntry(
            journalEntryId,
            request.AccountingTimestamp,
            request.Description,
            [
                new LedgerEntry(
                    Guid.NewGuid(),
                    journalEntryId,
                    request.AccountingTimestamp,
                    new LedgerAccount("Accrued Interest Receivable", LedgerAccountType.Asset),
                    125.44m,
                    0m,
                    request.Description,
                    lineDimensions),
                new LedgerEntry(
                    Guid.NewGuid(),
                    journalEntryId,
                    request.AccountingTimestamp,
                    new LedgerAccount("Interest Income", LedgerAccountType.Revenue),
                    0m,
                    125.44m,
                    request.Description,
                    lineDimensions)
            ],
            new JournalEntryMetadata(
                ActivityType: request.SourceEventType,
                LedgerBook: journalMetadataLedgerBookId.ToString("D"),
                EffectiveDate: request.EffectiveDate,
                IdempotencyKey: request.TreasuryContext?.IdempotencyKey));
        var command = new AccountingPostingCommandDto(
            Guid.NewGuid(),
            request.AggregateId,
            request.PeriodId,
            request.EffectiveDate,
            request.AccountingTimestamp,
            request.TreasuryContext?.IdempotencyKey ?? $"candidate:{request.SourceEventId:N}",
            SourceEventId: request.SourceEventId,
            CorrelationId: request.CorrelationId,
            SourceEventType: request.SourceEventType,
            TreasuryContext: request.TreasuryContext,
            ApprovalState: approvalState,
            LedgerBookId: request.LedgerBookId);
        var write = new LedgerJournalEntryWrite(
            entry,
            request.AggregateId,
            request.PeriodId,
            Guid.NewGuid(),
            request.CorrelationId,
            request.AccountingBasis,
            request.PolicyId ?? "gaap-accrual-v1",
            "v1",
            "accrual.interest-income",
            "v1",
            request.SourceEventId,
            request.SourceJournalEntryId,
            request.PostingKind,
            request.AdjustmentApproval,
            command,
            request.LedgerBookId);
        return new AccountingPostingCandidateWriteResult(BuildCandidateResult(request, command), write);
    }

    private static PostingRuleJournalCandidateResultDto BuildCandidateResult(
        PostingRuleJournalCandidateRequestDto request,
        AccountingPostingCommandDto command)
    {
        var dryRun = new RuleDryRunResultDto(
            request.FundProfileId,
            request.LedgerBookId,
            request.SourceEventType,
            request.EffectiveDate,
            request.EventAmount,
            request.Currency,
            IsPostingBalanced: true,
            SelectedRuleId: "posting.alpha-interest",
            RuleMatches:
            [
                new AccountingRuleDryRunMatchDto(
                    "posting.alpha-interest",
                    "Alpha interest accrual",
                    "v1",
                    100,
                    IsMatched: true,
                    Explanations: ["matched"],
                    ValidationIssues: [])
            ],
            GeneratedLines: [],
            ValidationIssues: []);
        return new PostingRuleJournalCandidateResultDto(
            dryRun,
            "posting.alpha-interest",
            "v1",
            GeneratedPostingLines: [],
            command,
            JournalEntryId: Guid.NewGuid(),
            TotalDebits: request.EventAmount,
            TotalCredits: request.EventAmount,
            Imbalance: 0m,
            IsBalanced: true,
            HasBlockingIssues: false,
            CanSubmitForApproval: true,
            CanPostWithoutAdditionalApproval: false,
            request.EvidenceLinks,
            Issues: []);
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

    private sealed class FixedCandidateWriteBuilder(AccountingPostingCandidateWriteResult result)
        : IAccountingPostingCandidateWriteBuilder
    {
        public Task<AccountingPostingCandidateWriteResult> BuildCandidateWriteAsync(
            PostingRuleJournalCandidateRequestDto request,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(result);
        }
    }

    private sealed record TypedCandidateHarness(
        AccountingPostingCandidateService Service,
        AccountingConfigurationService ConfigurationService,
        IAccountingJournalDraftService DraftService,
        PostingRuleJournalCandidateRequestDto Request);

    private sealed record AuthoritativeFactorHarness(
        AccountingPostingCandidateService Service,
        PostingRuleJournalCandidateRequestDto Request,
        StaticAssetOperationsQueryService AssetOperations);

    private sealed class StaticAssetOperationsQueryService : IAssetOperationsQueryService
    {
        public AssetOperationsDetailDto? Detail { get; set; }

        public Task<AssetOperationsDetailDto?> GetOperationsAsync(Guid securityId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(Detail?.Subject.SecurityId == securityId ? Detail : null);
        }

        public Task<AssetOperationsReadinessDto?> GetReadinessAsync(Guid securityId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(Detail?.Subject.SecurityId == securityId ? Detail.Readiness : null);
        }
    }

    private sealed class StaticLedgerBookService(
        LedgerBookDto book,
        LedgerPeriodDto period) : ILedgerBookService
    {
        public Task<LedgerBookDto> CreateBookAsync(CreateLedgerBookRequest request, CancellationToken ct = default)
            => Task.FromException<LedgerBookDto>(new NotSupportedException());

        public Task<LedgerBookDto?> GetBookAsync(Guid ledgerBookId, CancellationToken ct = default)
            => Task.FromResult<LedgerBookDto?>(ledgerBookId == book.LedgerBookId ? book : null);

        public Task<IReadOnlyList<LedgerBookDto>> ListBooksAsync(LedgerBookQuery query, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<LedgerBookDto>>([book]);

        public Task<LedgerPeriodDto> CreatePeriodAsync(CreateLedgerPeriodRequest request, CancellationToken ct = default)
            => Task.FromException<LedgerPeriodDto>(new NotSupportedException());

        public Task<IReadOnlyList<LedgerPeriodDto>> ListPeriodsAsync(LedgerPeriodQuery query, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<LedgerPeriodDto>>(
                !query.LedgerBookId.HasValue || query.LedgerBookId == book.LedgerBookId ? [period] : []);

        public Task<IReadOnlyList<LedgerPeriodDto>> ListOpenPeriodsAsync(Guid? ledgerBookId = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<LedgerPeriodDto>>(
                !ledgerBookId.HasValue || ledgerBookId == book.LedgerBookId ? [period] : []);

        public Task<LedgerPeriodSummaryDto?> GetPeriodSummaryAsync(Guid periodId, CancellationToken ct = default)
            => Task.FromResult<LedgerPeriodSummaryDto?>(null);

        public Task<LedgerPeriodCloseResultDto> ClosePeriodAsync(
            Guid periodId,
            CloseLedgerPeriodRequest request,
            CancellationToken ct = default)
            => Task.FromException<LedgerPeriodCloseResultDto>(new NotSupportedException());
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
