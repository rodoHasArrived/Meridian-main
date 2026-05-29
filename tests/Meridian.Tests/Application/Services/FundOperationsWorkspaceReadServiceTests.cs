using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.FundAccounts;
using Meridian.Application.SecurityMaster;
using Meridian.Application.Services;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Application.Services;

public sealed class FundOperationsWorkspaceReadServiceTests
{
    [Fact]
    public async Task GetWorkspaceAsync_WithRunsAccountsAndBanking_ReturnsAggregatedWorkspace()
    {
        var fundProfileId = $"fund-{Guid.NewGuid():N}";
        var fundId = TranslateFundProfileId(fundProfileId);
        var accountService = new InMemoryFundAccountService();
        var repository = new StrategyRunStore();
        var portfolioReadService = new PortfolioReadService();
        var securityMaster = new NullSecurityMasterQueryService();
        var service = new FundOperationsWorkspaceReadService(
            accountService,
            repository,
            portfolioReadService,
            new NavAttributionService(securityMaster),
            new ReportGenerationService(securityMaster));

        var bankAccount = await accountService.CreateAccountAsync(new CreateAccountRequest(
            AccountId: Guid.NewGuid(),
            AccountType: AccountTypeDto.Bank,
            AccountCode: "BANK-001",
            DisplayName: "Operating Cash",
            BaseCurrency: "USD",
            EffectiveFrom: new DateTimeOffset(2026, 4, 10, 0, 0, 0, TimeSpan.Zero),
            CreatedBy: "test",
            FundId: fundId,
            LedgerReference: "FUND-TB",
            BankDetails: new BankAccountDetailsDto(
                AccountNumber: "1234567890",
                BankName: "Meridian Bank",
                BranchName: null,
                Iban: null,
                BicSwift: null,
                RoutingNumber: null,
                SortCode: null,
                IntermediaryBankBic: null,
                IntermediaryBankName: null,
                BeneficiaryName: null,
                BeneficiaryAddress: null)));
        var custodyAccount = await accountService.CreateAccountAsync(new CreateAccountRequest(
            AccountId: Guid.NewGuid(),
            AccountType: AccountTypeDto.Custody,
            AccountCode: "CUST-001",
            DisplayName: "Core Custody",
            BaseCurrency: "USD",
            EffectiveFrom: new DateTimeOffset(2026, 4, 10, 0, 0, 0, TimeSpan.Zero),
            CreatedBy: "test",
            FundId: fundId,
            LedgerReference: "FUND-TB"));

        await accountService.RecordBalanceSnapshotAsync(new RecordAccountBalanceSnapshotRequest(
            AccountId: bankAccount.AccountId,
            AsOfDate: new DateOnly(2026, 4, 11),
            Currency: "USD",
            CashBalance: 2_500m,
            Source: "bank",
            RecordedBy: "test",
            PendingSettlement: 150m));
        await accountService.RecordBalanceSnapshotAsync(new RecordAccountBalanceSnapshotRequest(
            AccountId: custodyAccount.AccountId,
            AsOfDate: new DateOnly(2026, 4, 11),
            Currency: "USD",
            CashBalance: 750m,
            Source: "custody",
            RecordedBy: "test",
            SecuritiesMarketValue: 400m));
        await accountService.IngestBankStatementAsync(new IngestBankStatementRequest(
            BatchId: Guid.NewGuid(),
            AccountId: bankAccount.AccountId,
            StatementDate: new DateOnly(2026, 4, 11),
            BankName: "Meridian Bank",
            Notes: "test",
            Lines:
            [
                new BankStatementLineDto(
                    LineId: Guid.NewGuid(),
                    BatchId: Guid.NewGuid(),
                    AccountId: bankAccount.AccountId,
                    TransactionDate: new DateOnly(2026, 4, 11),
                    ValueDate: new DateOnly(2026, 4, 11),
                    Amount: 250m,
                    Currency: "USD",
                    TransactionType: "Contribution",
                    Description: "Capital contribution",
                    Reference: "BANK-REF-001",
                    ClosingBalance: 2_500m)
            ],
            LoadedBy: "test"));

        await repository.RecordRunAsync(BuildRun(
            runId: "run-governance-001",
            strategyId: "carry-1",
            strategyName: "Carry Strategy",
            fundProfileId: fundProfileId,
            fundDisplayName: "Alpha Income Fund"));

        var workspace = await service.GetWorkspaceAsync(new FundOperationsWorkspaceQuery(
            FundProfileId: fundProfileId,
            AsOf: new DateTimeOffset(2026, 4, 11, 16, 0, 0, TimeSpan.Zero),
            Currency: "USD"));

        workspace.DisplayName.Should().Be("Alpha Income Fund");
        workspace.RecordedRunCount.Should().Be(1);
        workspace.RelatedRunIds.Should().ContainSingle().Which.Should().Be("run-governance-001");
        workspace.Accounts.Should().HaveCount(2);
        workspace.BankSnapshots.Should().ContainSingle(snapshot => snapshot.AccountId == bankAccount.AccountId);
        workspace.CashFinancing.PendingSettlement.Should().Be(150m);
        workspace.Ledger.JournalEntryCount.Should().BeGreaterThan(0);
        workspace.Ledger.TrialBalance.Should().NotBeEmpty();
        workspace.LedgerReconciliationSnapshot.AsOf.Should().Be(new DateTimeOffset(2026, 4, 11, 16, 0, 0, TimeSpan.Zero));
        workspace.LedgerReconciliationSnapshot.Consolidated.JournalEntryCount.Should().BeGreaterThan(0);
        workspace.LedgerReconciliationSnapshot.Consolidated.LedgerEntryCount.Should().BeGreaterThan(0);
        workspace.LedgerReconciliationSnapshot.Consolidated.Balances.Should().NotBeEmpty();
        workspace.LedgerReconciliationSnapshot.Entities.Should().BeEmpty();
        workspace.LedgerReconciliationSnapshot.Sleeves.Should().BeEmpty();
        workspace.LedgerReconciliationSnapshot.Vehicles.Should().BeEmpty();
        workspace.Nav.ComponentCount.Should().BeGreaterThan(0);
        workspace.Reporting.ProfileCount.Should().BeGreaterThan(0);
        workspace.Workspace.TotalAccounts.Should().Be(2);
        workspace.Governance.Should().NotBeNull();
        workspace.Governance!.DecisionPosture.Should().NotBeNullOrWhiteSpace();
        workspace.Governance.SignoffPosture.Should().NotBeNullOrWhiteSpace();
        workspace.Governance.CloseReadiness.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetWorkspaceAsync_WithReportPackWorkflowService_ReturnsRestatementRecordsInReportingSummary()
    {
        var fundProfileId = $"fund-{Guid.NewGuid():N}";
        var fundId = TranslateFundProfileId(fundProfileId);
        var accountService = new InMemoryFundAccountService();
        var repository = new StrategyRunStore();
        var portfolioReadService = new PortfolioReadService();
        var securityMaster = new NullSecurityMasterQueryService();
        var workflowService = new ReportPackWorkflowService();
        var account = await accountService.CreateAccountAsync(new CreateAccountRequest(
            AccountId: Guid.NewGuid(),
            AccountType: AccountTypeDto.Custody,
            AccountCode: "CUST-RPT",
            DisplayName: "Report custody",
            BaseCurrency: "USD",
            EffectiveFrom: new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
            CreatedBy: "test",
            FundId: fundId,
            LedgerReference: "RPT-TB"));

        var report = workflowService.Create(
            fundProfileId,
            account.AccountId.ToString("D"),
            "2026-05",
            new VersionedReportTemplateIdDto("monthly-board-pack", 1),
            "reporter",
            [
                new ReportPackLineProvenanceDto(
                    "nav.total",
                    "ledger",
                    "ledger-entry-1",
                    "evidence-ledger-1",
                    CalculationNote: "NAV total ties to the retained ledger entry and matched reconciliation evidence.",
                    RunId: "run-restatement-1",
                    LedgerEntryId: "ledger-entry-1",
                    ReconciliationCaseId: "case-restatement-1",
                    ReportValue: "1250000",
                    SourceSessionId: "session-restatement-1",
                    ReconciliationRunId: "recon-run-restatement-1",
                    ProviderEventId: "provider-event-restatement-1",
                    SecurityMasterId: "security-master-nav",
                    SecurityDefinitionId: "security-definition-nav",
                    ReconciliationOutcome: "matched",
                    ApprovalId: "approval-restatement-1")
            ]);
        workflowService.Transition(report.ReportId, ReportPackWorkflowStateDto.Validated, "reviewer", "reviewer");
        workflowService.Transition(report.ReportId, ReportPackWorkflowStateDto.PendingApproval, "reviewer", "reviewer");
        workflowService.Transition(report.ReportId, ReportPackWorkflowStateDto.Approved, "approver", "approver");
        workflowService.Publish(
            report.ReportId,
            "publisher",
            "publisher",
            "fund-controller",
            "sha256:report-pack",
            "manifest-1",
            "vault/report-packs/manifest-1.json",
            [new ReportPackEvidenceLinkDto("evidence-ledger-1", "Ledger line", "/reporting/evidence?subject=report-pack", "ledger")]);
        var restated = workflowService.Restate(
            report.ReportId,
            "approver",
            "approver",
            "pricing-correction",
            "fund-controller",
            report.ReportId,
            [
                new ReportPackChangedLineDto(
                    "nav.total",
                    "1250000",
                    "1249500",
                    [new ReportPackEvidenceLinkDto("pricing-evidence-1", "Pricing override", "/reporting/evidence?subject=pricing", "pricing")])
            ]);
        var service = new FundOperationsWorkspaceReadService(
            accountService,
            repository,
            portfolioReadService,
            new NavAttributionService(securityMaster),
            new ReportGenerationService(securityMaster),
            reportPackWorkflowService: workflowService);

        var workspace = await service.GetWorkspaceAsync(new FundOperationsWorkspaceQuery(
            FundProfileId: fundProfileId,
            AsOf: new DateTimeOffset(2026, 5, 28, 16, 0, 0, TimeSpan.Zero),
            Currency: "USD"));

        workspace.Reporting.WorkflowRecords.Should().ContainSingle();
        var workflow = workspace.Reporting.WorkflowRecords!.Single();
        workflow.ReportId.Should().Be(restated.ReportId);
        workflow.State.Should().Be(ReportPackWorkflowStateDto.Restated);
        workflow.Restatement.Should().NotBeNull();
        workflow.Restatement!.ReasonCode.Should().Be("pricing-correction");
        workflow.Restatement.ChangedLines.Should().ContainSingle(line =>
            line.LineKey == "nav.total" &&
            line.PreviousValue == "1250000" &&
            line.CurrentValue == "1249500" &&
            line.EvidenceLinks!.Any(link => link.EvidenceId == "pricing-evidence-1"));
    }

    [Fact]
    public void ProjectReconciliationSnapshot_MapsConsolidatedAndPerDimensionSnapshots()
    {
        var asOf = new DateTimeOffset(2026, 4, 11, 16, 0, 0, TimeSpan.Zero);
        var cash = new LedgerAccount("Cash", LedgerAccountType.Asset);
        var revenue = new LedgerAccount("Revenue", LedgerAccountType.Revenue);
        var book = new FundLedgerBook("fund-projection");

        book.EntityLedger("entity-a").PostLines(asOf, "entity-sale", [(cash, 70m, 0m), (revenue, 0m, 70m)]);
        book.SleeveLedger("sleeve-core").PostLines(asOf, "sleeve-sale", [(cash, 20m, 0m), (revenue, 0m, 20m)]);
        book.VehicleLedger("vehicle-master").PostLines(asOf, "vehicle-sale", [(cash, 10m, 0m), (revenue, 0m, 10m)]);

        var projected = FundOperationsWorkspaceReadService.ProjectReconciliationSnapshot(
            book.ReconciliationSnapshot(asOf));

        projected.FundProfileId.Should().Be("fund-projection");
        projected.Consolidated.JournalEntryCount.Should().Be(3);
        projected.Consolidated.LedgerEntryCount.Should().Be(6);
        projected.Consolidated.Balances.Should().ContainSingle(line => line.AccountName == "Cash" && line.Balance == 100m);
        projected.Entities.Should().ContainKey("entity-a");
        projected.Entities["entity-a"].Balances.Should().ContainSingle(line => line.AccountName == "Cash" && line.Balance == 70m);
        projected.Sleeves.Should().ContainKey("sleeve-core");
        projected.Vehicles.Should().ContainKey("vehicle-master");
    }

    [Fact]
    public async Task GetWorkspaceAsync_WithBlankFundProfileId_ThrowsArgumentException()
    {
        var accountService = new InMemoryFundAccountService();
        var repository = new StrategyRunStore();
        var portfolioReadService = new PortfolioReadService();
        var securityMaster = new NullSecurityMasterQueryService();
        var service = new FundOperationsWorkspaceReadService(
            accountService,
            repository,
            portfolioReadService,
            new NavAttributionService(securityMaster),
            new ReportGenerationService(securityMaster));

        var act = () => service.GetWorkspaceAsync(new FundOperationsWorkspaceQuery(" "));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetWorkspaceAsync_WithSelectedLedgerIds_ConstrainsWorkspaceToSelection()
    {
        var fundProfileId = $"fund-{Guid.NewGuid():N}";
        var accountService = new InMemoryFundAccountService();
        var repository = new StrategyRunStore();
        var portfolioReadService = new PortfolioReadService();
        var securityMaster = new NullSecurityMasterQueryService();
        var service = new FundOperationsWorkspaceReadService(
            accountService,
            repository,
            portfolioReadService,
            new NavAttributionService(securityMaster),
            new ReportGenerationService(securityMaster));

        await repository.RecordRunAsync(BuildRun(
            runId: "run-selection-001",
            strategyId: "carry-1",
            strategyName: "Carry Strategy",
            fundProfileId: fundProfileId,
            fundDisplayName: "Alpha Income Fund"));
        await repository.RecordRunAsync(BuildRun(
            runId: "run-selection-002",
            strategyId: "carry-2",
            strategyName: "Carry Strategy 2",
            fundProfileId: fundProfileId,
            fundDisplayName: "Alpha Income Fund"));
        await repository.RecordRunAsync(BuildRun(
            runId: "run-selection-003",
            strategyId: "carry-3",
            strategyName: "Carry Strategy 3",
            fundProfileId: fundProfileId,
            fundDisplayName: "Alpha Income Fund"));

        var workspace = await service.GetWorkspaceAsync(new FundOperationsWorkspaceQuery(
            FundProfileId: fundProfileId,
            SelectedLedgerIds: ["run-selection-001", "run-selection-003"]));

        workspace.RecordedRunCount.Should().Be(2);
        workspace.RelatedRunIds.Should().BeEquivalentTo(["run-selection-001", "run-selection-003"]);
        workspace.Ledger.JournalEntryCount.Should().Be(4);
    }

    [Fact]
    public async Task GetWorkspaceAsync_WithEmptySelectedLedgerIds_MatchesUnfilteredWorkspace()
    {
        var fundProfileId = $"fund-{Guid.NewGuid():N}";
        var accountService = new InMemoryFundAccountService();
        var repository = new StrategyRunStore();
        var portfolioReadService = new PortfolioReadService();
        var securityMaster = new NullSecurityMasterQueryService();
        var service = new FundOperationsWorkspaceReadService(
            accountService,
            repository,
            portfolioReadService,
            new NavAttributionService(securityMaster),
            new ReportGenerationService(securityMaster));

        await repository.RecordRunAsync(BuildRun(
            runId: "run-all-001",
            strategyId: "carry-1",
            strategyName: "Carry Strategy",
            fundProfileId: fundProfileId,
            fundDisplayName: "Alpha Income Fund"));
        await repository.RecordRunAsync(BuildRun(
            runId: "run-all-002",
            strategyId: "carry-2",
            strategyName: "Carry Strategy 2",
            fundProfileId: fundProfileId,
            fundDisplayName: "Alpha Income Fund"));

        var fullWorkspace = await service.GetWorkspaceAsync(new FundOperationsWorkspaceQuery(FundProfileId: fundProfileId));
        var explicitEmptySelection = await service.GetWorkspaceAsync(new FundOperationsWorkspaceQuery(
            FundProfileId: fundProfileId,
            SelectedLedgerIds: Array.Empty<string>()));

        explicitEmptySelection.RecordedRunCount.Should().Be(fullWorkspace.RecordedRunCount);
        explicitEmptySelection.RelatedRunIds.Should().BeEquivalentTo(fullWorkspace.RelatedRunIds);
        explicitEmptySelection.Ledger.JournalEntryCount.Should().Be(fullWorkspace.Ledger.JournalEntryCount);
        explicitEmptySelection.Ledger.AssetBalance.Should().Be(fullWorkspace.Ledger.AssetBalance);
    }

    [Fact]
    public async Task GetWorkspaceAsync_WithUnknownSelectedLedgerIds_ReturnsEmptyLedgerProjection()
    {
        var fundProfileId = $"fund-{Guid.NewGuid():N}";
        var accountService = new InMemoryFundAccountService();
        var repository = new StrategyRunStore();
        var portfolioReadService = new PortfolioReadService();
        var securityMaster = new NullSecurityMasterQueryService();
        var service = new FundOperationsWorkspaceReadService(
            accountService,
            repository,
            portfolioReadService,
            new NavAttributionService(securityMaster),
            new ReportGenerationService(securityMaster));

        await repository.RecordRunAsync(BuildRun(
            runId: "run-known-001",
            strategyId: "carry-1",
            strategyName: "Carry Strategy",
            fundProfileId: fundProfileId,
            fundDisplayName: "Alpha Income Fund"));

        var workspace = await service.GetWorkspaceAsync(new FundOperationsWorkspaceQuery(
            FundProfileId: fundProfileId,
            SelectedLedgerIds: ["run-does-not-exist"]));

        workspace.RecordedRunCount.Should().Be(0);
        workspace.RelatedRunIds.Should().BeEmpty();
        workspace.Ledger.JournalEntryCount.Should().Be(0);
        workspace.Ledger.TrialBalance.Should().BeEmpty();
    }

    [Fact]
    public async Task PreviewReportPackAsync_WithCancelledToken_ThrowsOperationCanceledException()
    {
        var accountService = new InMemoryFundAccountService();
        var repository = new StrategyRunStore();
        var portfolioReadService = new PortfolioReadService();
        var securityMaster = new NullSecurityMasterQueryService();
        var service = new FundOperationsWorkspaceReadService(
            accountService,
            repository,
            portfolioReadService,
            new NavAttributionService(securityMaster),
            new ReportGenerationService(securityMaster));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => service.PreviewReportPackAsync(
            new FundReportPackPreviewRequestDto("fund-cancel"),
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GenerateReportPackAsync_WithDefaultFormats_WritesManifestProvenanceArtifactsAndChecksums()
    {
        var fundProfileId = $"fund-report-{Guid.NewGuid():N}";
        var accountService = new InMemoryFundAccountService();
        var strategyRepository = new StrategyRunStore();
        await strategyRepository.RecordRunAsync(BuildRun(
            runId: "run-report-001",
            strategyId: "report-1",
            strategyName: "Report Strategy",
            fundProfileId: fundProfileId,
            fundDisplayName: "Governed Report Fund"));

        var tempRoot = CreateTempDirectory();
        try
        {
            var repository = CreateReportPackRepository(tempRoot);
            var service = CreateReportPackService(accountService, strategyRepository, repository);

            var snapshot = await service.GenerateReportPackAsync(new FundReportPackGenerateRequestDto(
                FundProfileId: fundProfileId,
                AuditActor: "unit-test",
                AsOf: new DateTimeOffset(2026, 4, 11, 16, 0, 0, TimeSpan.Zero),
                CorrelationId: "corr-report-001",
                ExpectedSchemaVersion: GovernanceReportPackContract.CurrentSchemaVersion));

            snapshot.FundProfileId.Should().Be(fundProfileId);
            snapshot.ContractName.Should().Be(GovernanceReportPackContract.ContractName);
            snapshot.SchemaVersion.Should().Be(GovernanceReportPackContract.CurrentSchemaVersion);
            snapshot.AuditActor.Should().Be("unit-test");
            snapshot.CorrelationId.Should().Be("corr-report-001");
            snapshot.Status.Should().Be(GovernanceReportPackStatusDto.ReviewRequired);
            snapshot.ValidationIssues.Should().Contain(issue =>
                issue.Code == "report-pack.missing-security-master-classification" &&
                issue.Severity == GovernanceReportValidationSeverityDto.Warning);
            snapshot.LifecycleEvents.Select(static lifecycle => (
                    lifecycle.FromStatus,
                    lifecycle.ToStatus,
                    lifecycle.Actor,
                    lifecycle.CorrelationId))
                .Should()
                .ContainInOrder(
                    (GovernanceReportPackStatusDto.Draft, GovernanceReportPackStatusDto.Generated, "unit-test", "corr-report-001"),
                    (GovernanceReportPackStatusDto.Generated, GovernanceReportPackStatusDto.ReviewRequired, "unit-test", "corr-report-001"));
            snapshot.Provenance.RelatedRunIds.Should().ContainSingle().Which.Should().Be("run-report-001");
            snapshot.Provenance.SchemaVersion.Should().Be(GovernanceReportPackContract.CurrentSchemaVersion);
            snapshot.Provenance.JournalEntryCount.Should().BeGreaterThan(0);
            snapshot.Provenance.LedgerEntryCount.Should().BeGreaterThan(0);
            snapshot.Provenance.SourceSnapshotHash.Should().MatchRegex("^[a-f0-9]{64}$");
            snapshot.Provenance.LineagePointers.Should().Contain(pointer =>
                pointer.ScopeType == "report" &&
                pointer.ScopeKey == "summary" &&
                pointer.EvidenceType == "run" &&
                pointer.EvidenceId == "run-report-001" &&
                pointer.DisplayLabel == "Report Strategy (run-report-001)" &&
                pointer.Route == UiApiRoutes.WithParam(UiApiRoutes.RunsContinuity, "runId", "run-report-001") &&
                pointer.SourceSystem == "strategy-run");
            snapshot.Provenance.LineagePointers.Should().Contain(pointer =>
                pointer.ScopeType == "line" &&
                pointer.ScopeKey == "Securities:AAPL" &&
                pointer.EvidenceType == "ledger-account" &&
                pointer.DisplayLabel == "Securities / AAPL ledger line" &&
                pointer.Route == UiApiRoutes.WithQuery(
                    UiApiRoutes.WithParam(UiApiRoutes.RunsLedgerTrialBalance, "runId", "run-report-001"),
                    "accountName=Securities&symbol=AAPL") &&
                pointer.SourceSystem == "ledger");
            var securitiesLedgerPointer = snapshot.Provenance.LineagePointers.Should().ContainSingle(pointer =>
                    pointer.ScopeType == "line" &&
                    pointer.ScopeKey == "Securities:AAPL" &&
                    pointer.EvidenceType == "ledger-account")
                .Which;
            securitiesLedgerPointer.RelatedEvidenceIds.Should().ContainSingle(id => IsGuidString(id));
            securitiesLedgerPointer.EvidenceCount.Should().Be(1);
            securitiesLedgerPointer.Amount.Should().Be(400m);
            securitiesLedgerPointer.CapturedAt.Should().Be(new DateTimeOffset(2026, 4, 11, 14, 10, 0, TimeSpan.Zero));
            snapshot.Provenance.LineagePointers.Should().Contain(pointer =>
                pointer.ScopeType == "line" &&
                pointer.ScopeKey == "Securities:AAPL" &&
                pointer.EvidenceType == "security" &&
                pointer.EvidenceId == "AAPL" &&
                pointer.DisplayLabel == "AAPL" &&
                pointer.Route == UiApiRoutes.WithQuery(UiApiRoutes.WorkstationSecurityMasterSearch, "query=AAPL") &&
                pointer.SourceSystem == "security-master");
            var securityMasterPointer = snapshot.Provenance.LineagePointers.Should().ContainSingle(pointer =>
                    pointer.ScopeType == "line" &&
                    pointer.ScopeKey == "Securities:AAPL" &&
                    pointer.EvidenceType == "security")
                .Which;
            securityMasterPointer.RelatedEvidenceIds.Should().ContainSingle(id => IsGuidString(id));
            securityMasterPointer.EvidenceCount.Should().Be(1);
            securityMasterPointer.Amount.Should().Be(400m);
            securityMasterPointer.CapturedAt.Should().Be(new DateTimeOffset(2026, 4, 11, 14, 10, 0, TimeSpan.Zero));
            snapshot.Artifacts.Should().OnlyContain(artifact =>
                artifact.SchemaVersion == GovernanceReportPackContract.CurrentSchemaVersion);
            snapshot.Artifacts.Should().Contain(artifact => artifact.ArtifactKind == "trial-balance" && artifact.Format == GovernanceReportArtifactFormatDto.Json);
            snapshot.Artifacts.Should().Contain(artifact => artifact.ArtifactKind == "trial-balance" && artifact.Format == GovernanceReportArtifactFormatDto.Csv);
            snapshot.Artifacts.Should().Contain(artifact => artifact.ArtifactKind == "asset-class-sections" && artifact.Format == GovernanceReportArtifactFormatDto.Json);
            snapshot.Artifacts.Should().Contain(artifact => artifact.ArtifactKind == "asset-class-sections" && artifact.Format == GovernanceReportArtifactFormatDto.Csv);
            snapshot.Artifacts.Should().Contain(artifact => artifact.ArtifactKind == "workbook" && artifact.Format == GovernanceReportArtifactFormatDto.Xlsx);
            snapshot.Artifacts.Should().Contain(artifact => artifact.ArtifactKind == "provenance" && artifact.Format == GovernanceReportArtifactFormatDto.Json);

            foreach (var artifact in snapshot.Artifacts)
            {
                var path = ResolveArtifactPath(tempRoot, artifact);
                File.Exists(path).Should().BeTrue(path);
                var bytes = await File.ReadAllBytesAsync(path);
                bytes.LongLength.Should().Be(artifact.SizeBytes);
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant().Should().Be(artifact.ChecksumSha256);
            }

            var manifestPath = Directory.EnumerateFiles(
                    Path.Combine(tempRoot, "governance-report-packs"),
                    "manifest.json",
                    SearchOption.AllDirectories)
                .Should()
                .ContainSingle()
                .Which;
            File.ReadAllText(manifestPath).Should().Contain(snapshot.ReportId.ToString());
            File.ReadAllText(manifestPath).Should().Contain("\"schemaVersion\": 2");
            File.ReadAllText(manifestPath).Should().Contain("\"contractName\": \"governance-report-pack\"");
            File.ReadAllText(manifestPath).Should().Contain("\"status\": \"ReviewRequired\"");

            var trialBalanceCsv = snapshot.Artifacts.Single(artifact =>
                artifact.ArtifactKind == "trial-balance" && artifact.Format == GovernanceReportArtifactFormatDto.Csv);
            var csvLines = await File.ReadAllLinesAsync(ResolveArtifactPath(tempRoot, trialBalanceCsv));
            csvLines.Should().HaveCountGreaterThan(1);
            csvLines[0].Should().Be("accountName,accountType,symbol,currency,assetClass,primaryIdentifierKind,primaryIdentifierValue,subType,assetFamily,issuerType,riskCountry,lookupQuality,displayName,netBalance");
            csvLines.Skip(1).Select(static line => line.Split(',')[0])
                .Should()
                .ContainInOrder("Cash", "Securities", "Capital Account");

            var workbook = snapshot.Artifacts.Single(artifact => artifact.Format == GovernanceReportArtifactFormatDto.Xlsx);
            using var archive = ZipFile.OpenRead(ResolveArtifactPath(tempRoot, workbook));
            archive.GetEntry("xl/workbook.xml").Should().NotBeNull();
            archive.GetEntry("xl/worksheets/sheet1.xml").Should().NotBeNull();
            archive.GetEntry("xl/worksheets/sheet2.xml").Should().NotBeNull();
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task ReportPackHistory_ListsNewestFirstAndRetrievesById()
    {
        var fundProfileId = $"fund-history-{Guid.NewGuid():N}";
        var accountService = new InMemoryFundAccountService();
        var strategyRepository = new StrategyRunStore();
        await strategyRepository.RecordRunAsync(BuildRun(
            runId: "run-history-001",
            strategyId: "history-1",
            strategyName: "History Strategy",
            fundProfileId: fundProfileId,
            fundDisplayName: "History Fund"));

        var tempRoot = CreateTempDirectory();
        try
        {
            var service = CreateReportPackService(accountService, strategyRepository, CreateReportPackRepository(tempRoot));

            var older = await service.GenerateReportPackAsync(new FundReportPackGenerateRequestDto(
                FundProfileId: fundProfileId,
                AuditActor: "unit-test",
                AsOf: new DateTimeOffset(2026, 4, 10, 16, 0, 0, TimeSpan.Zero),
                Formats: [GovernanceReportArtifactFormatDto.Json]));
            await Task.Delay(10);
            var newer = await service.GenerateReportPackAsync(new FundReportPackGenerateRequestDto(
                FundProfileId: fundProfileId,
                AuditActor: "unit-test",
                AsOf: new DateTimeOffset(2026, 4, 11, 16, 0, 0, TimeSpan.Zero),
                Formats: [GovernanceReportArtifactFormatDto.Json]));

            var history = await service.GetReportPackHistoryAsync(fundProfileId, limit: 10);

            history.Should().HaveCount(2);
            history[0].GeneratedAt.Should().BeOnOrAfter(history[1].GeneratedAt);
            history.Select(item => item.ReportId).Should().ContainInOrder(newer.ReportId, older.ReportId);
            history.Should().OnlyContain(item =>
                item.Status == GovernanceReportPackStatusDto.ReviewRequired &&
                item.ValidationIssueCount > 0 &&
                item.LifecycleEventCount == 2);

            var detail = await service.GetReportPackAsync(newer.ReportId);
            detail.Should().NotBeNull();
            detail!.ReportId.Should().Be(newer.ReportId);
            detail.SchemaVersion.Should().Be(GovernanceReportPackContract.CurrentSchemaVersion);
            detail.Artifacts.Should().NotBeEmpty();
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task GenerateReportPackAsync_WithUnsupportedExpectedSchemaVersion_ThrowsArgumentException()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var service = CreateReportPackService(
                new InMemoryFundAccountService(),
                new StrategyRunStore(),
                CreateReportPackRepository(tempRoot));

            var act = () => service.GenerateReportPackAsync(new FundReportPackGenerateRequestDto(
                FundProfileId: "fund-schema-version",
                AuditActor: "unit-test",
                ExpectedSchemaVersion: GovernanceReportPackContract.CurrentSchemaVersion + 1));

            await act.Should()
                .ThrowAsync<ArgumentException>()
                .WithMessage("*Unsupported governance report-pack schema version*");
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task ReportPackRepository_WithFutureSchemaVersion_SkipsManifest_AndReadsLegacyV1Manifest()
    {
        var fundProfileId = $"fund-future-schema-{Guid.NewGuid():N}";
        var strategyRepository = new StrategyRunStore();
        await strategyRepository.RecordRunAsync(BuildRun(
            runId: "run-future-schema-001",
            strategyId: "future-1",
            strategyName: "Future Schema Strategy",
            fundProfileId: fundProfileId,
            fundDisplayName: "Future Schema Fund"));

        var tempRoot = CreateTempDirectory();
        try
        {
            var service = CreateReportPackService(
                new InMemoryFundAccountService(),
                strategyRepository,
                CreateReportPackRepository(tempRoot));
            var generated = await service.GenerateReportPackAsync(new FundReportPackGenerateRequestDto(
                FundProfileId: fundProfileId,
                AuditActor: "unit-test",
                Formats: [GovernanceReportArtifactFormatDto.Json]));
            var manifestPath = Directory.EnumerateFiles(
                    Path.Combine(tempRoot, "governance-report-packs"),
                    "manifest.json",
                    SearchOption.AllDirectories)
                .Single();
            var incompatible = generated with
            {
                SchemaVersion = GovernanceReportPackContract.CurrentSchemaVersion + 1
            };

            await File.WriteAllTextAsync(
                manifestPath,
                JsonSerializer.Serialize(incompatible, ReportPackJsonOptions()));

            var history = await service.GetReportPackHistoryAsync(fundProfileId);
            var detail = await service.GetReportPackAsync(generated.ReportId);

            history.Should().BeEmpty();
            detail.Should().BeNull();

            var legacy = generated with
            {
                SchemaVersion = GovernanceReportPackContract.MinimumReadableSchemaVersion,
                Provenance = generated.Provenance with { SchemaVersion = GovernanceReportPackContract.MinimumReadableSchemaVersion },
                Artifacts = generated.Artifacts
                    .Select(static artifact => artifact with { SchemaVersion = GovernanceReportPackContract.MinimumReadableSchemaVersion })
                    .ToArray(),
                Status = GovernanceReportPackStatusDto.Unknown,
                ValidationIssues = [],
                LifecycleEvents = []
            };
            await File.WriteAllTextAsync(
                manifestPath,
                JsonSerializer.Serialize(legacy, ReportPackJsonOptions()));

            var legacyHistory = await service.GetReportPackHistoryAsync(fundProfileId);
            var legacyDetail = await service.GetReportPackAsync(generated.ReportId);

            legacyHistory.Should().ContainSingle(item =>
                item.ReportId == generated.ReportId &&
                item.SchemaVersion == GovernanceReportPackContract.MinimumReadableSchemaVersion &&
                item.Status == GovernanceReportPackStatusDto.Unknown &&
                item.ValidationIssueCount == 0 &&
                item.LifecycleEventCount == 0);
            legacyDetail.Should().NotBeNull();
            legacyDetail!.SchemaVersion.Should().Be(GovernanceReportPackContract.MinimumReadableSchemaVersion);
            legacyDetail.Status.Should().Be(GovernanceReportPackStatusDto.Unknown);
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task GenerateReportPackAsync_WithEmptyFundData_WritesPackageWithWarnings()
    {
        var fundProfileId = $"fund-empty-{Guid.NewGuid():N}";
        var tempRoot = CreateTempDirectory();
        try
        {
            var service = CreateReportPackService(
                new InMemoryFundAccountService(),
                new StrategyRunStore(),
                CreateReportPackRepository(tempRoot));

            var snapshot = await service.GenerateReportPackAsync(new FundReportPackGenerateRequestDto(
                FundProfileId: fundProfileId,
                AuditActor: "unit-test",
                Formats: [GovernanceReportArtifactFormatDto.Json]));

            snapshot.Artifacts.Should().Contain(artifact => artifact.ArtifactKind == "trial-balance");
            snapshot.Artifacts.Should().Contain(artifact => artifact.ArtifactKind == "provenance");
            snapshot.Status.Should().Be(GovernanceReportPackStatusDto.ReviewRequired);
            snapshot.ValidationIssues.Should().Contain(issue =>
                issue.Code == "report-pack.no-contributing-runs" &&
                issue.Severity == GovernanceReportValidationSeverityDto.Warning);
            snapshot.ValidationIssues.Should().Contain(issue =>
                issue.Code == "report-pack.empty-trial-balance" &&
                issue.Severity == GovernanceReportValidationSeverityDto.Critical);
            snapshot.ValidationIssues.Should().Contain(issue =>
                issue.Code == "report-pack.missing-ledger-postings" &&
                issue.Severity == GovernanceReportValidationSeverityDto.Critical);
            snapshot.LifecycleEvents.Should().Contain(lifecycle =>
                lifecycle.FromStatus == GovernanceReportPackStatusDto.Generated &&
                lifecycle.ToStatus == GovernanceReportPackStatusDto.ReviewRequired);
            snapshot.Warnings.Should().Contain(warning => warning.Contains("No recorded fund-scoped runs", StringComparison.Ordinal));
            snapshot.Warnings.Should().Contain(warning => warning.Contains("no trial-balance rows", StringComparison.Ordinal));
            snapshot.Provenance.TrialBalanceLineCount.Should().Be(0);
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task GenerateReportPackAsync_WithCancelledToken_ThrowsOperationCanceledException()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var service = CreateReportPackService(
                new InMemoryFundAccountService(),
                new StrategyRunStore(),
                CreateReportPackRepository(tempRoot));
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var act = () => service.GenerateReportPackAsync(
                new FundReportPackGenerateRequestDto(
                    FundProfileId: "fund-cancel",
                    AuditActor: "unit-test"),
                cts.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    private static FundOperationsWorkspaceReadService CreateReportPackService(
        InMemoryFundAccountService accountService,
        StrategyRunStore strategyRepository,
        IGovernanceReportPackRepository reportPackRepository)
    {
        var securityMaster = new NullSecurityMasterQueryService();
        return new FundOperationsWorkspaceReadService(
            accountService,
            strategyRepository,
            new PortfolioReadService(),
            new NavAttributionService(securityMaster),
            new ReportGenerationService(securityMaster),
            reportPackRepository: reportPackRepository);
    }

    private static FileGovernanceReportPackRepository CreateReportPackRepository(string tempRoot) =>
        new(tempRoot, NullLogger<FileGovernanceReportPackRepository>.Instance);

    private static JsonSerializerOptions ReportPackJsonOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static string CreateTempDirectory()
    {
        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            "meridian-report-pack-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        return tempRoot;
    }

    private static string ResolveArtifactPath(string tempRoot, FundReportPackArtifactDto artifact) =>
        Path.Combine(
            tempRoot,
            "governance-report-packs",
            artifact.RelativePath.Replace('/', Path.DirectorySeparatorChar));

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static StrategyRunEntry BuildRun(
        string runId,
        string strategyId,
        string strategyName,
        string fundProfileId,
        string fundDisplayName)
    {
        var startedAt = new DateTimeOffset(2026, 4, 11, 14, 0, 0, TimeSpan.Zero);
        var completedAt = startedAt.AddMinutes(30);
        var ledger = CreateLedger();
        var positions = new Dictionary<string, Position>(StringComparer.OrdinalIgnoreCase)
        {
            ["AAPL"] = new("AAPL", 10, 40m, 0m, 0m)
        };
        var accountSnapshot = new FinancialAccountSnapshot(
            AccountId: BacktestDefaults.DefaultBrokerageAccountId,
            DisplayName: "Primary Brokerage",
            Kind: FinancialAccountKind.Brokerage,
            Institution: "Simulated Broker",
            Cash: 750m,
            MarginBalance: 0m,
            LongMarketValue: 400m,
            ShortMarketValue: 0m,
            Equity: 1_150m,
            Positions: positions,
            Rules: new FinancialAccountRules());
        var snapshot = new PortfolioSnapshot(
            Timestamp: completedAt,
            Date: DateOnly.FromDateTime(completedAt.UtcDateTime),
            Cash: 750m,
            MarginBalance: 0m,
            LongMarketValue: 400m,
            ShortMarketValue: 0m,
            TotalEquity: 1_150m,
            DailyReturn: 0m,
            Positions: positions,
            Accounts: new Dictionary<string, FinancialAccountSnapshot>(StringComparer.OrdinalIgnoreCase)
            {
                [accountSnapshot.AccountId] = accountSnapshot
            },
            DayCashFlows: []);

        var request = new BacktestRequest(
            From: new DateOnly(2026, 4, 10),
            To: new DateOnly(2026, 4, 11),
            Symbols: ["AAPL"],
            InitialCash: 1_000m,
            DataRoot: "./data");
        var metrics = new BacktestMetrics(
            InitialCapital: 1_000m,
            FinalEquity: 1_150m,
            GrossPnl: 150m,
            NetPnl: 150m,
            TotalReturn: 0.15m,
            AnnualizedReturn: 0.15m,
            SharpeRatio: 1.2,
            SortinoRatio: 1.2,
            CalmarRatio: 1.2,
            MaxDrawdown: 0m,
            MaxDrawdownPercent: 0m,
            MaxDrawdownRecoveryDays: 0,
            ProfitFactor: 1.0,
            WinRate: 1.0,
            TotalTrades: 1,
            WinningTrades: 1,
            LosingTrades: 0,
            TotalCommissions: 1m,
            TotalMarginInterest: 0m,
            TotalShortRebates: 0m,
            Xirr: 0.15,
            SymbolAttribution: new Dictionary<string, SymbolAttribution>
            {
                ["AAPL"] = new("AAPL", 150m, 0m, 1, 1m, 0m)
            });
        var result = new BacktestResult(
            Request: request,
            Universe: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "AAPL" },
            Snapshots: [snapshot],
            CashFlows: [],
            Fills: [],
            Metrics: metrics,
            Ledger: ledger,
            ElapsedTime: TimeSpan.FromSeconds(5),
            TotalEventsProcessed: 10);

        return StrategyRunEntry.Start(strategyId, strategyName, RunType.Paper) with
        {
            RunId = runId,
            StartedAt = startedAt,
            EndedAt = completedAt,
            Metrics = result,
            PortfolioId = $"{strategyId}-paper-portfolio",
            LedgerReference = $"{strategyId}-paper-ledger",
            AuditReference = $"audit-{runId}",
            FundProfileId = fundProfileId,
            FundDisplayName = fundDisplayName
        };
    }

    private static Meridian.Ledger.Ledger CreateLedger()
    {
        var ledger = new Meridian.Ledger.Ledger();
        PostBalancedEntry(ledger, new DateTimeOffset(2026, 4, 11, 14, 0, 0, TimeSpan.Zero), "Initial capital",
        [
            (LedgerAccounts.Cash, 1_000m, 0m),
            (LedgerAccounts.CapitalAccount, 0m, 1_000m)
        ]);
        PostBalancedEntry(ledger, new DateTimeOffset(2026, 4, 11, 14, 10, 0, TimeSpan.Zero), "Buy AAPL",
        [
            (LedgerAccounts.Securities("AAPL"), 400m, 0m),
            (LedgerAccounts.Cash, 0m, 400m)
        ]);
        return ledger;
    }

    private static void PostBalancedEntry(
        Meridian.Ledger.Ledger ledger,
        DateTimeOffset timestamp,
        string description,
        IReadOnlyList<(LedgerAccount Account, decimal Debit, decimal Credit)> lines)
    {
        var journalId = Guid.NewGuid();
        var ledgerLines = lines
            .Select(line => new LedgerEntry(
                Guid.NewGuid(),
                journalId,
                timestamp,
                line.Account,
                line.Debit,
                line.Credit,
                description))
            .ToArray();
        ledger.Post(new JournalEntry(journalId, timestamp, description, ledgerLines));
    }

    private static bool IsGuidString(string value) => Guid.TryParse(value, out _);

    private static Guid TranslateFundProfileId(string fundProfileId)
        => new(MD5.HashData(Encoding.UTF8.GetBytes(fundProfileId)));
}
