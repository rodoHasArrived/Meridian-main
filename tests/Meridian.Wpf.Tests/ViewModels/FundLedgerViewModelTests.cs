#if WINDOWS
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Controls;
using FluentAssertions;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Application.Services;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Reporting;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Meridian.Ui.Shared.Services;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;
using Meridian.Wpf.Workstation.Models;
using AppSecurityMasterQueryService = Meridian.Application.SecurityMaster.ISecurityMasterQueryService;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class FundLedgerViewModelTests
{
    [Fact]
    [Trait("Category", "W4Acceptance")]
    public void LoadAsync_PopulatesStructureLabels_SecurityCoverage_AndStrategyLevelReconciliation()
    {
        WpfTestThread.Run(async () =>
        {
            var storagePath = Path.Combine(
                Path.GetTempPath(),
                "meridian-fund-ledger-tests",
                $"{Guid.NewGuid():N}.json");

            try
            {
                var navigation = NavigationService.Instance;
                navigation.ResetForTests();
                navigation.Initialize(new Frame());

                var fundContext = new FundContextService(storagePath);
                await fundContext.UpsertProfileAsync(new FundProfileDetail(
                    FundProfileId: "alpha-fund",
                    DisplayName: "Alpha Fund",
                    LegalEntityName: "Alpha Fund LP",
                    BaseCurrency: "USD",
                    DefaultWorkspaceId: "governance",
                    DefaultLandingPageTag: "FundLedger",
                    DefaultLedgerScope: FundLedgerScope.Consolidated,
                    EntityIds: ["entity-alpha"],
                    SleeveIds: ["sleeve-credit"],
                    VehicleIds: ["vehicle-master"],
                    IsDefault: true));
                await fundContext.SelectFundProfileAsync("alpha-fund");

                var lookup = new StubSecurityReferenceLookup();
                lookup.Register("AAPL", new WorkstationSecurityReference(
                    SecurityId: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                    DisplayName: "Apple Inc.",
                    AssetClass: "Equity",
                    Currency: "USD",
                    Status: SecurityStatusDto.Active,
                    PrimaryIdentifier: "AAPL"));

                var store = new StrategyRunStore();
                await store.RecordRunAsync(BuildFundScopedRun("run-fund-ops"));

                var portfolioReadService = new PortfolioReadService(lookup);
                var ledgerReadService = new LedgerReadService(lookup);
                var runReadService = new StrategyRunReadService(store, portfolioReadService, ledgerReadService);
                var workspaceService = new StrategyRunWorkspaceService(store, runReadService, fundContext);
                var fakeApiClient = new FakeWorkstationReconciliationApiClient(
                [
                    BuildBreakQueueItem("run-fund-ops")
                ],
                [
                    BuildStrategyDetail("run-fund-ops")
                ]);

                var fundAccountService = new InMemoryFundAccountService();
                var fundId = ToFundId("alpha-fund");
                var entityId = Guid.Parse("11111111-1111-1111-1111-111111111111");
                var sleeveId = Guid.Parse("22222222-2222-2222-2222-222222222222");
                var vehicleId = Guid.Parse("33333333-3333-3333-3333-333333333333");
                var accountId = Guid.Parse("44444444-4444-4444-4444-444444444444");

                await fundAccountService.CreateAccountAsync(new CreateAccountRequest(
                    AccountId: accountId,
                    AccountType: AccountTypeDto.Custody,
                    AccountCode: "CUST-001",
                    DisplayName: "Primary Custody",
                    BaseCurrency: "USD",
                    EffectiveFrom: DateTimeOffset.UtcNow.AddDays(-10),
                    CreatedBy: "test",
                    EntityId: entityId,
                    FundId: fundId,
                    SleeveId: sleeveId,
                    VehicleId: vehicleId,
                    Institution: "Northern Trust"));
                await fundAccountService.RecordBalanceSnapshotAsync(new RecordAccountBalanceSnapshotRequest(
                    AccountId: accountId,
                    AsOfDate: new DateOnly(2026, 3, 21),
                    Currency: "USD",
                    CashBalance: 750m,
                    Source: "test",
                    RecordedBy: "test",
                    SecuritiesMarketValue: 250m));

                var fundAccountReadService = new FundAccountReadService(fundAccountService);
                var cashFinancingReadService = new CashFinancingReadService(workspaceService, fundAccountReadService);
                var reconciliationRepository = new InMemoryReconciliationRunRepository();
                var strategyReconciliationService = new ReconciliationRunService(
                    runReadService: runReadService,
                    projectionService: new ReconciliationProjectionService(),
                    repository: reconciliationRepository);
                var reconciliationReadService = new ReconciliationReadService(
                    fundAccountService,
                    fundAccountReadService,
                    workspaceService,
                    strategyReconciliationService);
                var workbenchService = new FundReconciliationWorkbenchService(
                    reconciliationReadService,
                    fundAccountService,
                    workspaceService,
                    fakeApiClient);
                var fundLedgerReadService = new FundLedgerReadService(workspaceService, fundContext, fundAccountReadService);
                var fundOperationsWorkspaceReadService = CreateFundOperationsWorkspaceReadService(
                    fundAccountService,
                    store,
                    portfolioReadService,
                    strategyReconciliationService);

                using var viewModel = new FundLedgerViewModel(
                    fundLedgerReadService,
                    fundContext,
                    navigation,
                    fundAccountReadService,
                    cashFinancingReadService,
                    workbenchService,
                    fundOperationsWorkspaceReadService,
                    workspaceService);

                await viewModel.LoadAsync();
                await WaitForConditionAsync(() => viewModel.SupportsSelectedBreakActions);

                viewModel.Accounts.Should().ContainSingle();
                viewModel.Accounts[0].StructureLabel.Should().Contain("Entity");
                viewModel.Accounts[0].StructureLabel.Should().Contain("Sleeve");
                viewModel.Accounts[0].StructureLabel.Should().Contain("Vehicle");

                viewModel.PortfolioPositions.Should().Contain(position =>
                    position.Symbol == "AAPL" &&
                    position.HasSecurityCoverage &&
                    position.SecurityDisplayName == "Apple Inc.");
                viewModel.PortfolioPositions.Should().Contain(position =>
                    position.Symbol == "TSLA" &&
                    !position.HasSecurityCoverage &&
                    position.CoverageLabel == "Unresolved");
                viewModel.SecurityCoverageText.Should().Contain("need Security Master coverage");

                viewModel.ReconciliationRuns.Should().Contain(item =>
                    item.ScopeLabel == "Strategy Run" &&
                    item.RunId == "run-fund-ops" &&
                    item.HasSecurityCoverageIssues &&
                    item.SecurityIssueCount == 2);
                viewModel.SelectedReconciliationQueueIndex.Should().Be(0);
                viewModel.IsOpenBreakQueueFilterSelected.Should().BeTrue();
                viewModel.ReconciliationBreakQueueItems.Should().ContainSingle(item =>
                    item.RunId == "run-fund-ops" &&
                    item.Status == ReconciliationBreakQueueStatus.Open);
                viewModel.ReconciliationCalibrationStatusText.Should().Be("Review Required");
                viewModel.ReconciliationCalibrationProfilesText.Should().Be("1");
                viewModel.ReconciliationCalibrationPendingSignoffText.Should().Be("1");
                viewModel.ReconciliationCalibrationMissingMetadataText.Should().Be("0");
                viewModel.ReconciliationCalibrationSummaryText.Should().Contain("sign-off");
                viewModel.ReconciliationCalibrationProfiles.Should().ContainSingle(profile =>
                    profile.ToleranceProfileId == "price-variance-ops" &&
                    profile.ExceptionRoute == "fund-ops-review" &&
                    profile.PendingSignoffCount == 1);
                viewModel.ReconciliationDetailTitle.Should().Be("Reconciliation Strategy");
                viewModel.ReconciliationSection.DetailTitle.Should().Be(viewModel.ReconciliationDetailTitle);
                viewModel.ReconciliationSection.CalibrationStatusText.Should().Be(viewModel.ReconciliationCalibrationStatusText);
                viewModel.ReconciliationDetailLifecycleText.Should().Contain("Start Review");
                viewModel.ReconciliationDetailLifecycleText.Should().Contain("records ownership");
                viewModel.ReconciliationDetailSignoffText.Should().Contain("Pending Signoff");
                viewModel.ReconciliationDetailSignoffText.Should().Contain("Fund operations lead");
                viewModel.ReconciliationNextBestActionText.Should().Contain("Attach broker statement evidence");
                viewModel.ReconciliationBlockerReasonText.Should().Contain("Broker statement market value");
                viewModel.ReconciliationBlockerReasonText.Should().Contain("Ledger impact:");
                viewModel.ReconciliationEvidenceLinksText.Should().Contain("statement-hash:price-gap");
                viewModel.ReconciliationSection.AccountingSignifierState.Kind.Should().Be(WorkstationStateKind.Blocked);
                viewModel.ReconciliationSection.AccountingSignifierState.ActionPosture!.Label.Should().Be("Start Review");
                viewModel.ReconciliationSection.AccountingSignifierState.SignoffRequirement!.Role.Should().Be("Fund operations lead");
                viewModel.ReconciliationSection.AccountingSignifierState.VisibleEvidenceLinks
                    .Should()
                    .Contain(link => link.Target.Contains("/api/workstation/reconciliation/break-queue/", StringComparison.Ordinal));
                viewModel.ReconciliationSection.AccountingSignifierState.VisibleEvidenceLinks
                    .Should()
                    .Contain(link => link.Label == "Explain the Break evidence" && link.Target.Contains("statement-hash:price-gap", StringComparison.Ordinal));
                viewModel.SupportsSelectedBreakActions.Should().BeTrue();
                viewModel.CanStartReviewSelectedBreak.Should().BeTrue();
                viewModel.CanResolveSelectedBreak.Should().BeFalse();
                viewModel.ReconciliationNoteText = "Investigating ledger drift";
                viewModel.ReconciliationSection.NoteText.Should().Be("Investigating ledger drift");
                viewModel.CanResolveSelectedBreak.Should().BeTrue();
                viewModel.OverviewStatusText.Should().Contain("unresolved security mapping");
            }
            finally
            {
                if (File.Exists(storagePath))
                {
                    File.Delete(storagePath);
                }
            }
        });
    }

    [Fact]
    [Trait("Category", "W4Acceptance")]
    public void ResolveSelectedBreakAsync_RefreshesQueueAndKeepsDecisionAuditVisible()
    {
        WpfTestThread.Run(async () =>
        {
            var storagePath = Path.Combine(
                Path.GetTempPath(),
                "meridian-fund-ledger-tests",
                $"{Guid.NewGuid():N}.json");

            try
            {
                var navigation = NavigationService.Instance;
                navigation.ResetForTests();
                navigation.Initialize(new Frame());

                var fundContext = new FundContextService(storagePath);
                await fundContext.UpsertProfileAsync(new FundProfileDetail(
                    FundProfileId: "alpha-fund",
                    DisplayName: "Alpha Fund",
                    LegalEntityName: "Alpha Fund LP",
                    BaseCurrency: "USD",
                    DefaultWorkspaceId: "governance",
                    DefaultLandingPageTag: "FundLedger",
                    DefaultLedgerScope: FundLedgerScope.Consolidated,
                    EntityIds: ["entity-alpha"],
                    SleeveIds: ["sleeve-credit"],
                    VehicleIds: ["vehicle-master"],
                    IsDefault: true));
                await fundContext.SelectFundProfileAsync("alpha-fund");

                var store = new StrategyRunStore();
                await store.RecordRunAsync(BuildFundScopedRun("run-fund-ops"));

                var portfolioReadService = new PortfolioReadService();
                var ledgerReadService = new LedgerReadService();
                var runReadService = new StrategyRunReadService(store, portfolioReadService, ledgerReadService);
                var workspaceService = new StrategyRunWorkspaceService(store, runReadService, fundContext);
                var fakeApiClient = new FakeWorkstationReconciliationApiClient(
                [
                    BuildBreakQueueItem("run-fund-ops")
                ],
                [
                    BuildStrategyDetail("run-fund-ops")
                ]);

                var fundAccountService = new InMemoryFundAccountService();
                var fundAccountReadService = new FundAccountReadService(fundAccountService);
                var cashFinancingReadService = new CashFinancingReadService(workspaceService, fundAccountReadService);
                var reconciliationRepository = new InMemoryReconciliationRunRepository();
                var strategyReconciliationService = new ReconciliationRunService(
                    runReadService: runReadService,
                    projectionService: new ReconciliationProjectionService(),
                    repository: reconciliationRepository);
                var reconciliationReadService = new ReconciliationReadService(
                    fundAccountService,
                    fundAccountReadService,
                    workspaceService,
                    strategyReconciliationService);
                var workbenchService = new FundReconciliationWorkbenchService(
                    reconciliationReadService,
                    fundAccountService,
                    workspaceService,
                    fakeApiClient);
                var fundLedgerReadService = new FundLedgerReadService(workspaceService, fundContext, fundAccountReadService);
                var fundOperationsWorkspaceReadService = CreateFundOperationsWorkspaceReadService(
                    fundAccountService,
                    store,
                    portfolioReadService,
                    strategyReconciliationService);

                using var viewModel = new FundLedgerViewModel(
                    fundLedgerReadService,
                    fundContext,
                    navigation,
                    fundAccountReadService,
                    cashFinancingReadService,
                    workbenchService,
                    fundOperationsWorkspaceReadService,
                    workspaceService);

                await viewModel.LoadAsync();
                await WaitForConditionAsync(() => viewModel.SupportsSelectedBreakActions);

                viewModel.ReconciliationNoteText = "Reviewed custodian statement and matched ledger adjustment.";
                await viewModel.StartReviewSelectedBreakAsync();
                await WaitForConditionAsync(() =>
                    viewModel.SelectedBreakQueueItem?.Status == ReconciliationBreakQueueStatus.InReview &&
                    viewModel.ReconciliationDetailLifecycleText.Contains("active review", StringComparison.OrdinalIgnoreCase));

                await viewModel.ResolveSelectedBreakAsync();
                await WaitForConditionAsync(() =>
                    viewModel.SelectedBreakQueueItem?.Status == ReconciliationBreakQueueStatus.Resolved &&
                    viewModel.ReconciliationDetailLifecycleText.Contains("closed by", StringComparison.OrdinalIgnoreCase) &&
                    viewModel.ReconciliationAuditRows.Any(row => row.Title == "Break closed"));

                viewModel.IsAllBreakQueueFilterSelected.Should().BeTrue();
                viewModel.ReconciliationBreakQueueItems.Should().ContainSingle(item =>
                    item.BreakId == "run-fund-ops:price-gap" &&
                    item.Status == ReconciliationBreakQueueStatus.Resolved &&
                    item.ResolutionNote == "Reviewed custodian statement and matched ledger adjustment.");
                viewModel.ReconciliationDetailSignoffText.Should().Contain("Decision captured");
                viewModel.ReconciliationDetailSignoffText.Should().Contain("close approval blocked");
                viewModel.ReconciliationAuditRows.Should().Contain(row =>
                    row.Title == "Break closed" &&
                    row.Description == "Reviewed custodian statement and matched ledger adjustment.");
                viewModel.ReconciliationActionFeedbackText.Should().Be("Break resolved and audit note captured.");
            }
            finally
            {
                if (File.Exists(storagePath))
                {
                    File.Delete(storagePath);
                }
            }
        });
    }

    [Fact]
    public void RefreshReconciliationWorkbenchAsync_PreservesRunSelection_AndLoadsAccountDetail()
    {
        WpfTestThread.Run(async () =>
        {
            var storagePath = Path.Combine(
                Path.GetTempPath(),
                "meridian-fund-ledger-tests",
                $"{Guid.NewGuid():N}.json");

            try
            {
                var navigation = NavigationService.Instance;
                navigation.ResetForTests();
                navigation.Initialize(new Frame());

                var fundContext = new FundContextService(storagePath);
                await fundContext.UpsertProfileAsync(new FundProfileDetail(
                    FundProfileId: "alpha-fund",
                    DisplayName: "Alpha Fund",
                    LegalEntityName: "Alpha Fund LP",
                    BaseCurrency: "USD",
                    DefaultWorkspaceId: "governance",
                    DefaultLandingPageTag: "FundLedger",
                    DefaultLedgerScope: FundLedgerScope.Consolidated,
                    EntityIds: ["entity-alpha"],
                    SleeveIds: ["sleeve-credit"],
                    VehicleIds: ["vehicle-master"],
                    IsDefault: true));
                await fundContext.SelectFundProfileAsync("alpha-fund");

                var lookup = new StubSecurityReferenceLookup();
                lookup.Register("AAPL", new WorkstationSecurityReference(
                    SecurityId: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                    DisplayName: "Apple Inc.",
                    AssetClass: "Equity",
                    Currency: "USD",
                    Status: SecurityStatusDto.Active,
                    PrimaryIdentifier: "AAPL"));

                var store = new StrategyRunStore();
                await store.RecordRunAsync(BuildFundScopedRun("run-fund-ops"));

                var portfolioReadService = new PortfolioReadService(lookup);
                var ledgerReadService = new LedgerReadService(lookup);
                var runReadService = new StrategyRunReadService(store, portfolioReadService, ledgerReadService);
                var workspaceService = new StrategyRunWorkspaceService(store, runReadService, fundContext);
                var fakeApiClient = new FakeWorkstationReconciliationApiClient(
                [
                    BuildBreakQueueItem("run-fund-ops")
                ],
                [
                    BuildStrategyDetail("run-fund-ops")
                ]);

                var fundAccountService = new InMemoryFundAccountService();
                var fundId = ToFundId("alpha-fund");
                var entityId = Guid.Parse("11111111-1111-1111-1111-111111111111");
                var sleeveId = Guid.Parse("22222222-2222-2222-2222-222222222222");
                var vehicleId = Guid.Parse("33333333-3333-3333-3333-333333333333");
                var accountId = Guid.Parse("55555555-5555-5555-5555-555555555555");

                await fundAccountService.CreateAccountAsync(new CreateAccountRequest(
                    AccountId: accountId,
                    AccountType: AccountTypeDto.Custody,
                    AccountCode: "CUST-002",
                    DisplayName: "Operations Custody",
                    BaseCurrency: "USD",
                    EffectiveFrom: DateTimeOffset.UtcNow.AddDays(-10),
                    CreatedBy: "test",
                    EntityId: entityId,
                    FundId: fundId,
                    SleeveId: sleeveId,
                    VehicleId: vehicleId,
                    Institution: "Northern Trust"));
                await fundAccountService.RecordBalanceSnapshotAsync(new RecordAccountBalanceSnapshotRequest(
                    AccountId: accountId,
                    AsOfDate: new DateOnly(2026, 3, 21),
                    Currency: "USD",
                    CashBalance: 750m,
                    Source: "test",
                    RecordedBy: "test",
                    SecuritiesMarketValue: 250m));
                await fundAccountService.ReconcileAccountAsync(new ReconcileAccountRequest(
                    AccountId: accountId,
                    AsOfDate: new DateOnly(2026, 3, 21),
                    RequestedBy: "test"));

                var fundAccountReadService = new FundAccountReadService(fundAccountService);
                var cashFinancingReadService = new CashFinancingReadService(workspaceService, fundAccountReadService);
                var reconciliationRepository = new InMemoryReconciliationRunRepository();
                var strategyReconciliationService = new ReconciliationRunService(
                    runReadService: runReadService,
                    projectionService: new ReconciliationProjectionService(),
                    repository: reconciliationRepository);
                var reconciliationReadService = new ReconciliationReadService(
                    fundAccountService,
                    fundAccountReadService,
                    workspaceService,
                    strategyReconciliationService);
                var workbenchService = new FundReconciliationWorkbenchService(
                    reconciliationReadService,
                    fundAccountService,
                    workspaceService,
                    fakeApiClient);
                var fundLedgerReadService = new FundLedgerReadService(workspaceService, fundContext, fundAccountReadService);
                var fundOperationsWorkspaceReadService = CreateFundOperationsWorkspaceReadService(
                    fundAccountService,
                    store,
                    portfolioReadService,
                    strategyReconciliationService);

                using var viewModel = new FundLedgerViewModel(
                    fundLedgerReadService,
                    fundContext,
                    navigation,
                    fundAccountReadService,
                    cashFinancingReadService,
                    workbenchService,
                    fundOperationsWorkspaceReadService,
                    workspaceService);

                await viewModel.LoadAsync();
                await WaitForConditionAsync(() => viewModel.ReconciliationRunItems.Any(item => item.SourceType == FundReconciliationSourceType.AccountRun));

                var accountRun = viewModel.ReconciliationRunItems.Single(item => item.SourceType == FundReconciliationSourceType.AccountRun);
                viewModel.SelectedReconciliationQueueIndex = 1;
                viewModel.SelectedReconciliationRun = accountRun;

                await WaitForConditionAsync(() =>
                    viewModel.ReconciliationDetailTitle == accountRun.PrimaryLabel &&
                    viewModel.CanOpenSelectedReconciliationAccountWorkflow);

                await viewModel.RefreshReconciliationWorkbenchAsync();
                await WaitForConditionAsync(() =>
                    viewModel.SelectedReconciliationRun?.RowKey == accountRun.RowKey &&
                    viewModel.ReconciliationDetailTitle == accountRun.PrimaryLabel &&
                    viewModel.CanOpenSelectedReconciliationAccountWorkflow);

                viewModel.SelectedReconciliationQueueIndex.Should().Be(1);
                viewModel.SelectedReconciliationRun.Should().NotBeNull();
                viewModel.SelectedReconciliationRun!.RowKey.Should().Be(accountRun.RowKey);
                viewModel.ReconciliationDetailTitle.Should().Be(accountRun.PrimaryLabel);
                viewModel.SupportsSelectedBreakActions.Should().BeFalse();
                viewModel.CanOpenSelectedReconciliationAccountWorkflow.Should().BeTrue();
            }
            finally
            {
                if (File.Exists(storagePath))
                {
                    File.Delete(storagePath);
                }
            }
        });
    }

    [Fact]
    public void MatchSelectedReconciliationItemsAsync_RequiresBothSidesAndResolvesBreak()
    {
        WpfTestThread.Run(async () =>
        {
            var storagePath = Path.Combine(
                Path.GetTempPath(),
                "meridian-fund-ledger-tests",
                $"{Guid.NewGuid():N}.json");

            try
            {
                var navigation = NavigationService.Instance;
                navigation.ResetForTests();
                navigation.Initialize(new Frame());

                var fundContext = new FundContextService(storagePath);
                await fundContext.UpsertProfileAsync(new FundProfileDetail(
                    FundProfileId: "alpha-fund",
                    DisplayName: "Alpha Fund",
                    LegalEntityName: "Alpha Fund LP",
                    BaseCurrency: "USD",
                    DefaultWorkspaceId: "governance",
                    DefaultLandingPageTag: "FundLedger",
                    DefaultLedgerScope: FundLedgerScope.Consolidated,
                    EntityIds: ["entity-alpha"],
                    SleeveIds: ["sleeve-credit"],
                    VehicleIds: ["vehicle-master"],
                    IsDefault: true));
                await fundContext.SelectFundProfileAsync("alpha-fund");

                var store = new StrategyRunStore();
                await store.RecordRunAsync(BuildFundScopedRun("run-fund-ops"));

                var portfolioReadService = new PortfolioReadService();
                var ledgerReadService = new LedgerReadService();
                var runReadService = new StrategyRunReadService(store, portfolioReadService, ledgerReadService);
                var workspaceService = new StrategyRunWorkspaceService(store, runReadService, fundContext);
                var fakeApiClient = new FakeWorkstationReconciliationApiClient(
                [
                    BuildBreakQueueItem("run-fund-ops")
                ],
                [
                    BuildStrategyDetail("run-fund-ops")
                ]);

                var fundAccountService = new InMemoryFundAccountService();
                var fundAccountReadService = new FundAccountReadService(fundAccountService);
                var cashFinancingReadService = new CashFinancingReadService(workspaceService, fundAccountReadService);
                var reconciliationRepository = new InMemoryReconciliationRunRepository();
                var strategyReconciliationService = new ReconciliationRunService(
                    runReadService: runReadService,
                    projectionService: new ReconciliationProjectionService(),
                    repository: reconciliationRepository);
                var reconciliationReadService = new ReconciliationReadService(
                    fundAccountService,
                    fundAccountReadService,
                    workspaceService,
                    strategyReconciliationService);
                var workbenchService = new FundReconciliationWorkbenchService(
                    reconciliationReadService,
                    fundAccountService,
                    workspaceService,
                    fakeApiClient);
                var fundLedgerReadService = new FundLedgerReadService(workspaceService, fundContext, fundAccountReadService);
                var fundOperationsWorkspaceReadService = CreateFundOperationsWorkspaceReadService(
                    fundAccountService,
                    store,
                    portfolioReadService,
                    strategyReconciliationService);

                using var viewModel = new FundLedgerViewModel(
                    fundLedgerReadService,
                    fundContext,
                    navigation,
                    fundAccountReadService,
                    cashFinancingReadService,
                    workbenchService,
                    fundOperationsWorkspaceReadService,
                    workspaceService);

                await viewModel.LoadAsync();
                await WaitForConditionAsync(() => viewModel.ReconciliationEntryRows.Count > 0 && viewModel.ReconciliationSourceDataRows.Count > 0);

                viewModel.CanMatchSelectedReconciliationItems.Should().BeFalse();
                viewModel.ReconciliationEntryRows.Should().ContainSingle(row => row.CheckLabel == "TSLA market value");
                viewModel.ReconciliationSourceDataRows.Should().ContainSingle(row => row.CheckLabel == "TSLA market value");

                viewModel.ReconciliationEntryRows[0].IsSelected = true;
                viewModel.CanMatchSelectedReconciliationItems.Should().BeFalse();

                viewModel.ReconciliationSourceDataRows[0].IsSelected = true;
                viewModel.CanMatchSelectedReconciliationItems.Should().BeTrue();
                viewModel.ReconciliationMatchSelectionText.Should().Contain("1 ledger entry row(s)");
                viewModel.ReconciliationMatchSelectionText.Should().Contain("1 source-data row(s)");

                await viewModel.MatchSelectedReconciliationItemsAsync();
                await WaitForConditionAsync(() =>
                    viewModel.SelectedBreakQueueItem?.Status == ReconciliationBreakQueueStatus.Resolved &&
                    viewModel.ReconciliationAuditRows.Any(row => row.Title == "Break closed"));

                viewModel.ReconciliationActionFeedbackText.Should().Be("Selected ledger entries and source data marked reconciled.");
                viewModel.ReconciliationBreakQueueItems.Should().ContainSingle(item =>
                    item.BreakId == "run-fund-ops:price-gap" &&
                    item.Status == ReconciliationBreakQueueStatus.Resolved &&
                    !string.IsNullOrWhiteSpace(item.ResolutionNote) &&
                    item.ResolutionNote.Contains("Matched ledger entry selection", StringComparison.Ordinal) &&
                    item.ResolutionNote.Contains("TSLA market value", StringComparison.Ordinal));
            }
            finally
            {
                if (File.Exists(storagePath))
                {
                    File.Delete(storagePath);
                }
            }
        });
    }

    [Fact]
    public void ResetReconciliationFiltersCommand_RestoresLoadedBreakQueueRows()
    {
        WpfTestThread.Run(async () =>
        {
            var storagePath = Path.Combine(
                Path.GetTempPath(),
                "meridian-fund-ledger-tests",
                $"{Guid.NewGuid():N}.json");

            try
            {
                var navigation = NavigationService.Instance;
                navigation.ResetForTests();
                navigation.Initialize(new Frame());

                var fundContext = new FundContextService(storagePath);
                await fundContext.UpsertProfileAsync(new FundProfileDetail(
                    FundProfileId: "alpha-fund",
                    DisplayName: "Alpha Fund",
                    LegalEntityName: "Alpha Fund LP",
                    BaseCurrency: "USD",
                    DefaultWorkspaceId: "governance",
                    DefaultLandingPageTag: "FundLedger",
                    DefaultLedgerScope: FundLedgerScope.Consolidated,
                    EntityIds: ["entity-alpha"],
                    SleeveIds: ["sleeve-credit"],
                    VehicleIds: ["vehicle-master"],
                    IsDefault: true));
                await fundContext.SelectFundProfileAsync("alpha-fund");

                var store = new StrategyRunStore();
                await store.RecordRunAsync(BuildFundScopedRun("run-fund-ops"));

                var portfolioReadService = new PortfolioReadService();
                var ledgerReadService = new LedgerReadService();
                var runReadService = new StrategyRunReadService(store, portfolioReadService, ledgerReadService);
                var workspaceService = new StrategyRunWorkspaceService(store, runReadService, fundContext);
                var fakeApiClient = new FakeWorkstationReconciliationApiClient(
                [
                    BuildBreakQueueItem("run-fund-ops")
                ],
                [
                    BuildStrategyDetail("run-fund-ops")
                ]);

                var fundAccountService = new InMemoryFundAccountService();
                var fundAccountReadService = new FundAccountReadService(fundAccountService);
                var cashFinancingReadService = new CashFinancingReadService(workspaceService, fundAccountReadService);
                var reconciliationRepository = new InMemoryReconciliationRunRepository();
                var strategyReconciliationService = new ReconciliationRunService(
                    runReadService: runReadService,
                    projectionService: new ReconciliationProjectionService(),
                    repository: reconciliationRepository);
                var reconciliationReadService = new ReconciliationReadService(
                    fundAccountService,
                    fundAccountReadService,
                    workspaceService,
                    strategyReconciliationService);
                var workbenchService = new FundReconciliationWorkbenchService(
                    reconciliationReadService,
                    fundAccountService,
                    workspaceService,
                    fakeApiClient);
                var fundLedgerReadService = new FundLedgerReadService(workspaceService, fundContext, fundAccountReadService);
                var fundOperationsWorkspaceReadService = CreateFundOperationsWorkspaceReadService(
                    fundAccountService,
                    store,
                    portfolioReadService,
                    strategyReconciliationService);

                using var viewModel = new FundLedgerViewModel(
                    fundLedgerReadService,
                    fundContext,
                    navigation,
                    fundAccountReadService,
                    cashFinancingReadService,
                    workbenchService,
                    fundOperationsWorkspaceReadService,
                    workspaceService);

                await viewModel.LoadAsync();
                await WaitForConditionAsync(() =>
                    viewModel.ReconciliationBreakQueueItems.Count == 1);

                viewModel.HasActiveReconciliationFilters.Should().BeFalse();
                viewModel.ResetReconciliationFiltersCommand.CanExecute(null).Should().BeFalse();

                viewModel.SelectedReconciliationScopeFilterIndex = 2;
                viewModel.ReconciliationSearchText = "does-not-exist";

                viewModel.ReconciliationBreakQueueItems.Should().BeEmpty();
                viewModel.ReconciliationBreakQueueEmptyStateText.Should().Contain("Reset filters");
                viewModel.HasActiveReconciliationFilters.Should().BeTrue();
                viewModel.ResetReconciliationFiltersCommand.CanExecute(null).Should().BeTrue();

                viewModel.ResetReconciliationFiltersCommand.Execute(null);

                viewModel.ReconciliationSearchText.Should().BeEmpty();
                viewModel.SelectedReconciliationScopeFilterIndex.Should().Be(0);
                viewModel.IsOpenBreakQueueFilterSelected.Should().BeTrue();
                viewModel.HasActiveReconciliationFilters.Should().BeFalse();
                viewModel.ResetReconciliationFiltersCommand.CanExecute(null).Should().BeFalse();
                viewModel.ReconciliationBreakQueueItems.Should().ContainSingle(item =>
                    item.RunId == "run-fund-ops" &&
                    item.Status == ReconciliationBreakQueueStatus.Open);
                viewModel.ReconciliationActionFeedbackText.Should().Contain("filters reset");
            }
            finally
            {
                if (File.Exists(storagePath))
                {
                    File.Delete(storagePath);
                }
            }
        });
    }

    [Fact]
    public void FundLedgerPage_ReconciliationToolbar_BindsResetFiltersRecoveryAction()
    {
        var xaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\FundLedgerPage.xaml"));

        xaml.Should().NotContain("EmbeddedShellHeroCardStyle");
        xaml.Should().Contain("FundLedgerActionStrip");
        xaml.Should().Contain("FundLedgerActionStripState");
        xaml.Should().Contain("FundLedgerAccountWorkbench");
        xaml.Should().Contain("Table=\"{Binding AccountQueueTable}\"");
        xaml.Should().Contain("Inspector=\"{Binding SelectedAccountInspector}\"");
        xaml.Should().Contain("FundLedgerAccountQueueGrid");
        xaml.Should().Contain("FundLedgerAccountSelectionInspector");
        xaml.Should().Contain("FundLedgerAccountActionInspector");
        xaml.Should().Contain("ToolTip=\"{Binding SelectedAccountPortfolioTooltip}\"");
        xaml.Should().Contain("ToolTipService.ShowOnDisabled=\"True\"");
        xaml.Should().Contain("ReconciliationResetFiltersButton");
        xaml.Should().Contain("Command=\"{Binding ResetReconciliationFiltersCommand}\"");
        xaml.Should().Contain("ReconciliationRefreshQueueButton");
        xaml.Should().Contain("ReconciliationDetailLifecycleText");
        xaml.Should().Contain("ReconciliationDetailSignoffText");
        xaml.Should().Contain("FundReconciliationAccountingSignifier");
        xaml.Should().Contain("ReconciliationSection.AccountingSignifierState");
        xaml.Should().NotContain("FundReconciliationGovernanceSignifier");
        xaml.Should().NotContain("ReconciliationSection.GovernanceSignifierState");
        xaml.Should().Contain("FundReportPackReadinessSignifier");
        xaml.Should().Contain("ReportPackReadinessState");
        xaml.Should().Contain("FundPrivateCapitalCloseReadinessSignifier");
        xaml.Should().Contain("FundPrivateCapitalCloseLaneGrid");
        xaml.Should().Contain("FundPrivateCapitalNavSupportGrid");
        xaml.Should().Contain("FundPrivateCapitalCloseApprovalGrid");
        xaml.Should().Contain("PrivateCapitalCloseReadinessState");
        xaml.Should().Contain("ItemsSource=\"{Binding PrivateCapitalCloseLanes}\"");
        xaml.Should().Contain("ItemsSource=\"{Binding PrivateCapitalNavSupportPackages}\"");
        xaml.Should().Contain("ItemsSource=\"{Binding PrivateCapitalCloseApprovals}\"");
    }

    [Fact]
    public void FundLedgerAccountInspectorProjection_ShouldFailClosedWithoutSelectionAndSurfaceAccountFacts()
    {
        var empty = FundLedgerViewModel.BuildSelectedAccountInspector(null);

        empty.Title.Should().Be("No account selected");
        empty.Subtitle.Should().Contain("blocked");
        empty.Detail.Should().Contain("Select an account row");

        var account = new FundAccountSummary(
            AccountId: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            AccountType: AccountTypeDto.Brokerage,
            AccountCode: "BRK-100",
            DisplayName: "Prime Brokerage Alpha",
            BaseCurrency: "USD",
            Institution: "Broker A",
            IsActive: true,
            CashBalance: 12_500m,
            SecuritiesMarketValue: 42_000m,
            NetAssetValue: 54_500m,
            LastSnapshotDate: new DateOnly(2026, 4, 25),
            ReconciliationRuns: 3,
            OpenBreaks: 2,
            PortfolioId: "portfolio-alpha",
            LedgerReference: "ledger-alpha",
            BankName: "Bank A",
            AccountNumberMasked: "****100",
            StructureLabel: "Entity alpha",
            WorkflowLabel: "Run alpha");

        var selected = FundLedgerViewModel.BuildSelectedAccountInspector(account);

        selected.Title.Should().Be("Prime Brokerage Alpha");
        selected.Badge.Should().NotBeNull();
        selected.Badge!.Value.Should().Be("Active");
        selected.Facts.Select(fact => fact.Label)
            .Should()
            .Contain(["Code", "Currency", "Structure", "Workflow", "Cash", "NAV", "Open breaks", "Last snapshot"]);
        selected.Facts.Single(fact => fact.Label == "Open breaks").Value.Should().Be("2");
    }

    [Fact]
    public void FundLedgerViewModelSource_ShouldOpenCanonicalAccountingShell()
    {
        var source = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\ViewModels\FundLedgerViewModel.cs"));

        source.Should().Contain("OpenAccountingCommand = new RelayCommand(() => _navigationService.NavigateTo(\"AccountingShell\"))");
        source.Should().Contain("OpenGovernanceCommand => OpenAccountingCommand");
        source.Should().NotContain("_navigationService.NavigateTo(\"GovernanceShell\")");
    }

    [Fact]
    public void FundLedgerAccountingDrillInCopy_ShouldUseCanonicalAccountingLabels()
    {
        var xaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\FundLedgerPage.xaml"));
        var source = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\ViewModels\FundLedgerViewModel.cs"));
        var sectionSource = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\ViewModels\FundLedgerViewModel.Sections.cs"));
        var reconciliationSource = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\ViewModels\FundLedgerViewModel.Reconciliation.cs"));

        xaml.Should().Contain("AutomationProperties.Name=\"Accounting Route Banner\"");
        xaml.Should().Contain("Accounting Report-Pack Preview");
        xaml.Should().Contain("inside Accounting");
        xaml.Should().Contain("leave the Accounting workspace");
        xaml.Should().NotContain("AutomationProperties.Name=\"Governance Route Banner\"");
        xaml.Should().NotContain("Governance Report-Pack Preview");
        xaml.Should().NotContain("inside governance");
        xaml.Should().NotContain("leave the governance workspace");

        source.Should().Contain("Accounting fund operations are loaded for the active fund profile.");
        source.Should().Contain("Accounting operator");
        source.Should().Contain("_accountingLifecycle");
        source.Should().Contain("accountingWorkspaceTask");
        source.Should().Contain("accountingWorkspace");
        source.Should().Contain("\"shared-accounting\"");
        source.Should().NotContain("Governance fund operations are loaded for the active fund profile.");
        source.Should().NotContain("Governance operator");
        source.Should().NotContain("governance artifacts");
        source.Should().NotContain("shared governance workbench");
        source.Should().NotContain("_governanceLifecycle");
        source.Should().NotContain("governanceWorkspaceTask");
        source.Should().NotContain("var governanceWorkspace");
        source.Should().NotContain("\"shared-governance\"");

        sectionSource.Should().Contain("Accounting operator sign-off is pending.");
        sectionSource.Should().Contain("read-only in Accounting");
        sectionSource.Should().NotContain("Governance operator sign-off is pending.");
        sectionSource.Should().NotContain("read-only in Governance");

        reconciliationSource.Should().Contain("read-only in Accounting");
        reconciliationSource.Should().Contain("accounting ownership");
        reconciliationSource.Should().Contain("accounting queue");
        reconciliationSource.Should().NotContain("read-only in Governance");
        reconciliationSource.Should().NotContain("governance ownership");
        reconciliationSource.Should().NotContain("governance queue");
    }

    [Fact]
    public void FundLedgerReconciliationSection_ShouldOwnQueueAndDetailCollectionsWithAdapterBindings()
    {
        var sectionSource = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\ViewModels\FundLedgerViewModel.Sections.cs"));
        var reconciliationSource = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\ViewModels\FundLedgerViewModel.Reconciliation.cs"));

        sectionSource.Should().Contain("public ObservableCollection<FundReconciliationBreakQueueRow> BreakQueueItems");
        sectionSource.Should().Contain("public ObservableCollection<FundReconciliationMatchCandidateRow> EntryRows");
        sectionSource.Should().Contain("public ObservableCollection<FundReconciliationMatchCandidateRow> SourceDataRows");
        sectionSource.Should().Contain("public ObservableCollection<FundReconciliationCheckDetailRow> ExceptionRows");
        sectionSource.Should().Contain("public IReadOnlyList<FundReconciliationBreakQueueRow> AllBreakQueueItems");
        sectionSource.Should().Contain("public FundReconciliationQueueView SelectedQueueView");
        sectionSource.Should().Contain("public FundReconciliationBreakQueueFilter SelectedBreakQueueFilter");
        sectionSource.Should().Contain("public FundReconciliationBreakQueueRow? SelectedBreakQueueItem");
        reconciliationSource.Should().Contain("public ObservableCollection<FundReconciliationBreakQueueRow> ReconciliationBreakQueueItems => ReconciliationSection.BreakQueueItems");
        reconciliationSource.Should().Contain("public ObservableCollection<FundReconciliationMatchCandidateRow> ReconciliationEntryRows => ReconciliationSection.EntryRows");
        reconciliationSource.Should().Contain("public ObservableCollection<FundReconciliationMatchCandidateRow> ReconciliationSourceDataRows => ReconciliationSection.SourceDataRows");
        reconciliationSource.Should().Contain("public async Task MatchSelectedReconciliationItemsAsync");
        reconciliationSource.Should().Contain("get => (int)ReconciliationSection.SelectedQueueView");
        reconciliationSource.Should().Contain("ReconciliationSection.SelectedBreakQueueFilter = filter");
        reconciliationSource.Should().Contain("SynchronizeCollection(ReconciliationSection.BreakQueueItems");
        reconciliationSource.Should().NotContain("_reconciliationBreakQueueItems");
        reconciliationSource.Should().NotContain("_allReconciliationBreakQueueItems");
        reconciliationSource.Should().NotContain("_selectedReconciliationQueueView");
        reconciliationSource.Should().NotContain("_selectedReconciliationBreakQueueFilter");
        reconciliationSource.Should().NotContain("_selectedBreakQueueItem");
    }

    [Fact]
    public void FundLedgerPage_ReconciliationMatchItemsTab_BindsTwoSelectableGrids()
    {
        var xaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\FundLedgerPage.xaml"));
        var codeBehind = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\FundLedgerPage.xaml.cs"));

        xaml.Should().Contain("Header=\"Match Items\"");
        xaml.Should().Contain("ReconciliationEntryMatchGrid");
        xaml.Should().Contain("ItemsSource=\"{Binding ReconciliationEntryRows}\"");
        xaml.Should().Contain("ReconciliationSourceMatchGrid");
        xaml.Should().Contain("ItemsSource=\"{Binding ReconciliationSourceDataRows}\"");
        xaml.Should().Contain("IsEnabled=\"{Binding CanMatchSelectedReconciliationItems}\"");
        xaml.Should().Contain("AutomationProperties.AutomationId=\"ReconciliationMatchSelectedItemsButton\"");
        codeBehind.Should().Contain("MatchSelectedReconciliationItemsAsync");
    }


    [Fact]
    public void FundLedgerPage_StatementReconciliationPanel_BindsListDetailAndSharedAffordances()
    {
        var xaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\FundLedgerPage.xaml"));

        xaml.Should().Contain("StatementReconciliationWorkbench");
        xaml.Should().Contain("StatementRuns");
        xaml.Should().Contain("StatementValidationIssues");
        xaml.Should().Contain("StatementUnresolvedBreaks");
        xaml.Should().Contain("StatementCaseActions");
        xaml.Should().Contain("SelectedStatementRun");
        xaml.Should().Contain("SelectedStatementBreak");
        xaml.Should().Contain("StatementDisabledReasonText");
        xaml.Should().Contain("ReconciliationSection.StatementSignifierState");
        xaml.Should().Contain("RefreshStatementReconciliationCommand");
    }

    [Fact]
    public void FundLedgerStatementReconciliationSection_ShouldUseSharedEndpointReadModelsAndVmOwnedState()
    {
        var sectionSource = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\ViewModels\FundLedgerViewModel.Sections.cs"));
        var viewModelSource = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\ViewModels\FundLedgerViewModel.StatementReconciliation.cs"));
        var serviceSource = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Services\StatementReconciliationWorkbenchService.cs"));

        sectionSource.Should().Contain("public ObservableCollection<StatementRunWorkbenchRow> StatementRuns");
        sectionSource.Should().Contain("public ObservableCollection<StatementValidationIssueRow> StatementValidationIssues");
        sectionSource.Should().Contain("public ObservableCollection<StatementUnresolvedBreakRow> StatementUnresolvedBreaks");
        sectionSource.Should().Contain("public ObservableCollection<StatementCaseActionRow> StatementCaseActions");
        sectionSource.Should().Contain("public StatementRunWorkbenchRow? SelectedStatementRun");
        sectionSource.Should().Contain("public StatementUnresolvedBreakRow? SelectedStatementBreak");
        sectionSource.Should().Contain("public string StatementDisabledReasonText");
        viewModelSource.Should().Contain("RefreshStatementReconciliationCommand");
        viewModelSource.Should().Contain("BuildCaseActionRows");
        viewModelSource.Should().Contain("BuildStatementBreakSignifier");
        serviceSource.Should().Contain("GetStatementRunsAsync");
        serviceSource.Should().Contain("GetStatementExceptionsAsync");
        serviceSource.Should().Contain("GetOpenStatementBreaksAsync");
        serviceSource.Should().Contain("GetOpenReconciliationCasesAsync");
        serviceSource.Should().Contain("GetReconciliationQueueStatusAsync");
    }

    [Fact]
    public void FundLedgerWorkbenchSection_ShouldOwnRouteChromeAndReportPackPresentationWithAdapterBindings()
    {
        var sectionSource = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\ViewModels\FundLedgerViewModel.Sections.cs"));
        var viewModelSource = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\ViewModels\FundLedgerViewModel.cs"));

        sectionSource.Should().Contain("internal sealed class FundLedgerWorkbenchSectionViewModel");
        sectionSource.Should().Contain("public string CurrentWorkbenchModeText");
        sectionSource.Should().Contain("public string RouteBannerTitleText");
        sectionSource.Should().Contain("public bool HasRouteBanner");
        sectionSource.Should().Contain("public string ReconciliationOwnershipText");
        sectionSource.Should().Contain("public string ReportPackOwnershipText");
        sectionSource.Should().Contain("public WorkstationStateModel ReportPackReadinessState");
        sectionSource.Should().Contain("public ObservableCollection<FundAccountingRecordEvidenceCategoryRow> AccountingRecordEvidenceCategories");
        sectionSource.Should().Contain("public ObservableCollection<FundPrivateCapitalCloseLaneRow> PrivateCapitalCloseLanes");
        sectionSource.Should().Contain("public ObservableCollection<FundPrivateCapitalNavSupportPackageRow> PrivateCapitalNavSupportPackages");
        sectionSource.Should().Contain("public ObservableCollection<FundPrivateCapitalCloseApprovalRow> PrivateCapitalCloseApprovals");
        sectionSource.Should().Contain("public string AccountingRecordSummaryText");
        sectionSource.Should().Contain("public WorkstationStateModel AccountingRecordReadinessState");
        sectionSource.Should().Contain("public string PrivateCapitalCloseSummaryText");
        sectionSource.Should().Contain("public WorkstationStateModel PrivateCapitalCloseReadinessState");
        sectionSource.Should().Contain("public int SelectedTabIndex");
        viewModelSource.Should().Contain("internal FundLedgerWorkbenchSectionViewModel WorkbenchSection");
        viewModelSource.Should().Contain("get => WorkbenchSection.CurrentWorkbenchModeText");
        viewModelSource.Should().Contain("get => WorkbenchSection.RouteBannerTitleText");
        viewModelSource.Should().Contain("get => WorkbenchSection.ReportPackReadinessState");
        viewModelSource.Should().Contain("get => WorkbenchSection.AccountingRecordReadinessState");
        viewModelSource.Should().Contain("get => WorkbenchSection.PrivateCapitalCloseReadinessState");
        viewModelSource.Should().Contain("IPrivateCapitalCloseCockpitService?");
        viewModelSource.Should().Contain("LoadPrivateCapitalCloseCockpitAsync");
        viewModelSource.Should().Contain("ApplyPrivateCapitalCloseCockpit");
        viewModelSource.Should().Contain("get => WorkbenchSection.SelectedTabIndex");
        viewModelSource.Should().NotContain("private string _currentWorkbenchModeText");
        viewModelSource.Should().NotContain("private string _routeBannerTitleText");
        viewModelSource.Should().NotContain("private bool _hasRouteBanner");
        viewModelSource.Should().NotContain("private string _reconciliationOwnershipText");
        viewModelSource.Should().NotContain("private string _reportPackOwnershipText");
        viewModelSource.Should().NotContain("private WorkstationStateModel _reportPackReadinessState");
        viewModelSource.Should().NotContain("private WorkstationStateModel _accountingRecordReadinessState");
        viewModelSource.Should().NotContain("private WorkstationStateModel _privateCapitalCloseReadinessState");
        viewModelSource.Should().NotContain("private int _selectedTabIndex");
    }
    [Fact]
    [Trait("Category", "W4Acceptance")]
    public void LoadAsync_ReportPackContext_LoadsPreviewAndSelectsReportPackTab()
    {
        WpfTestThread.Run(async () =>
        {
            var storagePath = Path.Combine(
                Path.GetTempPath(),
                "meridian-fund-ledger-tests",
                $"{Guid.NewGuid():N}.json");

            try
            {
                var navigation = NavigationService.Instance;
                navigation.ResetForTests();
                navigation.Initialize(new Frame());

                var fundContext = new FundContextService(storagePath);
                await fundContext.UpsertProfileAsync(new FundProfileDetail(
                    FundProfileId: "alpha-fund",
                    DisplayName: "Alpha Fund",
                    LegalEntityName: "Alpha Fund LP",
                    BaseCurrency: "USD",
                    DefaultWorkspaceId: "governance",
                    DefaultLandingPageTag: "FundLedger",
                    DefaultLedgerScope: FundLedgerScope.Consolidated,
                    EntityIds: ["entity-alpha"],
                    SleeveIds: ["sleeve-credit"],
                    VehicleIds: ["vehicle-master"],
                    IsDefault: true));
                await fundContext.SelectFundProfileAsync("alpha-fund");

                var store = new StrategyRunStore();
                await store.RecordRunAsync(BuildFundScopedRun("run-report-pack"));

                var fundAccountService = new InMemoryFundAccountService();
                var fundId = ToFundId("alpha-fund");
                var entityId = Guid.Parse("77777777-7777-7777-7777-777777777777");
                var sleeveId = Guid.Parse("88888888-8888-8888-8888-888888888888");
                var vehicleId = Guid.Parse("99999999-9999-9999-9999-999999999999");
                var accountId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

                await fundAccountService.CreateAccountAsync(new CreateAccountRequest(
                    AccountId: accountId,
                    AccountType: AccountTypeDto.Custody,
                    AccountCode: "CUST-RPT",
                    DisplayName: "Reporting Custody",
                    BaseCurrency: "USD",
                    EffectiveFrom: DateTimeOffset.UtcNow.AddDays(-5),
                    CreatedBy: "test",
                    EntityId: entityId,
                    FundId: fundId,
                    SleeveId: sleeveId,
                    VehicleId: vehicleId,
                    Institution: "Northern Trust"));
                await fundAccountService.RecordBalanceSnapshotAsync(new RecordAccountBalanceSnapshotRequest(
                    AccountId: accountId,
                    AsOfDate: new DateOnly(2026, 3, 21),
                    Currency: "USD",
                    CashBalance: 750m,
                    Source: "test",
                    RecordedBy: "test",
                    SecuritiesMarketValue: 250m));

                var operationsContinuityService = CreateOperationsContinuityWorkflowService();
                var evidence = new OperationsEvidenceLinkDto(
                    "ev-wpf-accounting-record",
                    "Retained broker source packet",
                    "/api/workstation/operations/continuity/evidence/ev-wpf-accounting-record",
                    "operations-continuity",
                    new DateTimeOffset(2026, 5, 8, 14, 20, 0, TimeSpan.Zero));
                var start = await operationsContinuityService.StartWorkflowAsync(new OperationsStartWorkflowRequestDto(
                    FundAccountId: accountId,
                    PeriodId: "2026-05",
                    SecurityMasterSnapshotId: Guid.NewGuid(),
                    BrokerSource: "custodian",
                    Actor: "wpf-test",
                    EvidenceLinks: [evidence]));
                var imported = await operationsContinuityService.ImportBrokerDataAsync(
                    start.Workflow!.WorkflowId,
                    new OperationsTransitionRequestDto(start.Workflow.Version, "wpf-test", EvidenceLinks: [evidence]));
                await operationsContinuityService.NormalizeBrokerTransactionsAsync(
                    start.Workflow.WorkflowId,
                    new OperationsTransitionRequestDto(imported.Workflow!.Version, "wpf-test", EvidenceLinks: [evidence]));

                var portfolioReadService = new PortfolioReadService();
                var runReadService = new StrategyRunReadService(store, portfolioReadService, new LedgerReadService());
                var workspaceService = new StrategyRunWorkspaceService(store, runReadService, fundContext);
                var fundAccountReadService = new FundAccountReadService(fundAccountService);
                var fundLedgerReadService = new FundLedgerReadService(workspaceService, fundContext, fundAccountReadService);
                var cashFinancingReadService = new CashFinancingReadService(workspaceService, fundAccountReadService);
                var reconciliationRepository = new InMemoryReconciliationRunRepository();
                var strategyReconciliationService = new ReconciliationRunService(
                    runReadService: runReadService,
                    projectionService: new ReconciliationProjectionService(),
                    repository: reconciliationRepository);
                var reconciliationReadService = new ReconciliationReadService(
                    fundAccountService,
                    fundAccountReadService,
                    workspaceService,
                    strategyReconciliationService);
                var workbenchService = new FundReconciliationWorkbenchService(
                    reconciliationReadService,
                    fundAccountService,
                    workspaceService,
                    new FakeWorkstationReconciliationApiClient([], []));
                var fundOperationsWorkspaceReadService = CreateFundOperationsWorkspaceReadService(
                    fundAccountService,
                    store,
                    portfolioReadService,
                    strategyReconciliationService,
                    operationsContinuityService);
                var closeCockpitService = new StubPrivateCapitalCloseCockpitService(BuildPrivateCapitalCloseCockpit());

                using var viewModel = new FundLedgerViewModel(
                    fundLedgerReadService,
                    fundContext,
                    navigation,
                    fundAccountReadService,
                    cashFinancingReadService,
                    workbenchService,
                    fundOperationsWorkspaceReadService,
                    workspaceService,
                    privateCapitalCloseCockpitService: closeCockpitService);

                await viewModel.LoadAsync(new FundOperationsNavigationContext(
                    Tab: FundOperationsTab.ReportPack,
                    FundProfileId: "alpha-fund"));
                await WaitForConditionAsync(() =>
                    viewModel.ReportPackAssetSections.Any() &&
                    viewModel.ReportPackStatusText.Contains("preview is ready", StringComparison.OrdinalIgnoreCase));

                viewModel.SelectedTabIndex.Should().Be((int)FundOperationsTab.ReportPack);
                viewModel.HasRouteBanner.Should().BeTrue();
                viewModel.RouteBannerTitleText.Should().Contain("Report Pack");
                viewModel.CurrentWorkbenchTitleText.Should().Contain("Report Pack");
                viewModel.ReportPackTrialBalanceLinesText.Should().NotBe("0");
                viewModel.ReportPackGeneratedAtText.Should().NotBe("-");
                viewModel.ReportPackAssetSections.Should().NotBeEmpty();
                viewModel.ReportPackStatusText.Should().Contain("preview is ready for operator handoff");
                viewModel.ReportPackOwnershipText.Should().Contain("final report-pack sign-off");
                viewModel.ReportPackOwnershipText.Should().Contain("Close readiness remains blocked");
                viewModel.ReportPackSnapshotWarningText.Should().Contain("Snapshot confirmed");
                viewModel.ReportPackSnapshotWarningText.Should().Contain("Refresh again before sign-off");
                viewModel.CurrentWorkbenchSubtitleText.Should().Contain("handoff readiness");
                viewModel.CurrentWorkbenchSubtitleText.Should().Contain("sign-off posture");
                viewModel.ReportPackReadinessState.Kind.Should().Be(WorkstationStateKind.Ready);
                viewModel.ReportPackReadinessState.Title.Should().Be("Report pack evidence linked");
                viewModel.ReportPackReadinessState.Detail.Should().Contain("preview is ready for operator handoff");
                viewModel.ReportPackReadinessState.Detail.Should().Contain("Close readiness remains blocked until shared lifecycle gates, reconciliation decisions, and report-pack evidence align.");
                viewModel.ReportPackReadinessState.ReadinessTone.Should().Be(WorkstationReadinessTone.EvidenceLinked);
                viewModel.ReportPackReadinessState.ActionPosture!.Label.Should().Be("Review handoff");
                viewModel.ReportPackReadinessState.ActionPosture.Target.Should().Be("FundReportPack");
                viewModel.ReportPackReadinessState.ActionPosture.Detail.Should().Contain("preview freshness");
                viewModel.ReportPackReadinessState.VisibleEvidenceLinks.Should().Contain(link => link.Label == "Report-pack preview");
                viewModel.ReportPackReadinessState.VisibleEvidenceLinks.Should().Contain(link => link.Label == "Audit traceability");
                viewModel.ReportPackReadinessState.RecoveryActions.Should().Contain(action =>
                    action.Label == "Refresh before distribution" &&
                    action.Detail.Contains("approval change", StringComparison.OrdinalIgnoreCase));
                viewModel.ReportPackReadinessState.SignoffRequirement!.Role.Should().NotBeNullOrWhiteSpace();
                viewModel.ReportPackReadinessState.SignoffRequirement.Status.Should().Contain("Close readiness");
                viewModel.ReportPackReadinessState.SignoffRequirement.Detail.Should().Contain("current preview");
                viewModel.AccountingRecordEvidenceCategories.Should().HaveCount(8);
                viewModel.AccountingRecordEvidenceCategories.Should().Contain(row =>
                    row.Key == "source-records" &&
                    row.Label == "Retained source data" &&
                    !row.IsComplete &&
                    row.SourceTarget == "OperationsContinuity" &&
                    row.EvidenceSubject.StartsWith("accounting-record/accounting-record-", StringComparison.Ordinal) &&
                    row.EvidenceSubjectTarget == $"EvidenceWorkbench:{row.EvidenceSubject}");
                viewModel.AccountingRecordEvidenceCategories.Should().Contain(row =>
                    row.Key == "exports" &&
                    row.StatusLabel == "Review required" &&
                    row.RequiredEvidenceLabel.Contains("export manifest", StringComparison.OrdinalIgnoreCase) &&
                    row.RequiredEvidenceLabel.Contains("retained evidence hash", StringComparison.OrdinalIgnoreCase) &&
                    row.SourceTarget == "FundReportPack");
                viewModel.AccountingRecordStatusText.Should().Be("Review required");
                viewModel.AccountingRecordEvidenceText.Should().Contain("0/8 evidence categories complete");
                viewModel.AccountingRecordEvidenceText.Should().Contain("60s target");
                viewModel.AccountingRecordReadinessState.Title.Should().Be("Accounting record review required");
                viewModel.AccountingRecordReadinessState.TargetText.Should().Be("OperationsContinuity");
                viewModel.AccountingRecordReadinessState.Detail.Should().Contain("0 of 8 required evidence categories complete");
                viewModel.AccountingRecordReadinessState.VisibleEvidenceLinks.Should().Contain(link =>
                    link.Label == "Retained source data" &&
                    link.Target == "OperationsContinuity" &&
                    link.Source.StartsWith("accounting-record/accounting-record-", StringComparison.Ordinal));
                closeCockpitService.RequestedFundProfileId.Should().Be("alpha-fund");
                viewModel.PrivateCapitalCloseStatusText.Should().Be("Review Required");
                viewModel.PrivateCapitalCloseSummaryText.Should().Contain("1/2 close lanes ready");
                viewModel.PrivateCapitalCloseEvidenceText.Should().Contain("4 evidence links");
                viewModel.PrivateCapitalCloseBlockerText.Should().Contain("Controller approval required");
                viewModel.PrivateCapitalCloseLanes.Should().Contain(row =>
                    row.LaneId == "partner-capital-tie-out" &&
                    row.StatusLabel == "Review Required" &&
                    row.RequiredActionsLabel.Contains("Approve partner capital tie-out", StringComparison.OrdinalIgnoreCase) &&
                    row.SourceTarget == "OperationsClose");
                viewModel.PrivateCapitalNavSupportPackages.Should().ContainSingle(row =>
                    row.PackageId == "nav-pack-alpha" &&
                    row.StatusLabel == "Ready" &&
                    row.ShadowNavLabel.Contains("USD", StringComparison.OrdinalIgnoreCase) &&
                    row.ComponentLabel == "1/1 components ready");
                viewModel.PrivateCapitalCloseApprovals.Should().ContainSingle(row =>
                    row.ApprovalId == "approval-close-alpha" &&
                    row.StatusLabel == "Approved" &&
                    row.ReviewerLabel == "controller" &&
                    row.EvidenceLabel == "1 evidence link");
                viewModel.PrivateCapitalCloseReadinessState.Title.Should().Be("Private-capital close review required");
                viewModel.PrivateCapitalCloseReadinessState.ActionPosture!.Target.Should().Be("OperationsClose");
                viewModel.PrivateCapitalCloseReadinessState.VisibleEvidenceLinks.Should().Contain(link =>
                    link.Label == "NAV support package" &&
                    link.Target == "FundReportPack");
                viewModel.PrivateCapitalCloseReadinessState.RecoveryActions.Should().Contain(action =>
                    action.Label == "Approve partner capital tie-out" &&
                    action.Target == "OperationsClose");
            }
            finally
            {
                if (File.Exists(storagePath))
                {
                    File.Delete(storagePath);
                }
            }
        });
    }

    [Fact]
    public void LoadAsync_PopulatesCashProjectionAndScopedLedgerDimensions()
    {
        WpfTestThread.Run(async () =>
        {
            var storagePath = Path.Combine(
                Path.GetTempPath(),
                "meridian-fund-ledger-tests",
                $"{Guid.NewGuid():N}.json");

            try
            {
                var navigation = NavigationService.Instance;
                navigation.ResetForTests();
                navigation.Initialize(new Frame());

                var fundContext = new FundContextService(storagePath);
                await fundContext.UpsertProfileAsync(new FundProfileDetail(
                    FundProfileId: "alpha-fund",
                    DisplayName: "Alpha Fund",
                    LegalEntityName: "Alpha Fund LP",
                    BaseCurrency: "USD",
                    DefaultWorkspaceId: "governance",
                    DefaultLandingPageTag: "FundLedger",
                    DefaultLedgerScope: FundLedgerScope.Consolidated,
                    EntityIds: ["entity-alpha"],
                    SleeveIds: ["sleeve-credit"],
                    VehicleIds: ["vehicle-master"],
                    IsDefault: true));
                await fundContext.SelectFundProfileAsync("alpha-fund");

                var fundAccountService = new InMemoryFundAccountService();
                var fundId = ToFundId("alpha-fund");
                var entityId = Guid.Parse("12121212-1111-1111-1111-111111111111");
                var sleeveId = Guid.Parse("23232323-2222-2222-2222-222222222222");
                var vehicleId = Guid.Parse("34343434-3333-3333-3333-333333333333");
                var accountId = Guid.Parse("45454545-4444-4444-4444-444444444444");

                await fundAccountService.CreateAccountAsync(new CreateAccountRequest(
                    AccountId: accountId,
                    AccountType: AccountTypeDto.Custody,
                    AccountCode: "CUST-SCOPE",
                    DisplayName: "Scoped Custody",
                    BaseCurrency: "USD",
                    EffectiveFrom: DateTimeOffset.UtcNow.AddDays(-10),
                    CreatedBy: "test",
                    EntityId: entityId,
                    FundId: fundId,
                    SleeveId: sleeveId,
                    VehicleId: vehicleId,
                    Institution: "Northern Trust"));
                await fundAccountService.RecordBalanceSnapshotAsync(new RecordAccountBalanceSnapshotRequest(
                    AccountId: accountId,
                    AsOfDate: new DateOnly(2026, 3, 21),
                    Currency: "USD",
                    CashBalance: 750m,
                    Source: "test",
                    RecordedBy: "test",
                    SecuritiesMarketValue: 250m));

                var store = new StrategyRunStore();
                await store.RecordRunAsync(BuildFundScopedRun("run-scoped-ledger", accountId.ToString()));

                var portfolioReadService = new PortfolioReadService();
                var ledgerReadService = new LedgerReadService();
                var runReadService = new StrategyRunReadService(store, portfolioReadService, ledgerReadService);
                var workspaceService = new StrategyRunWorkspaceService(store, runReadService, fundContext);
                var fundAccountReadService = new FundAccountReadService(fundAccountService);
                var fundLedgerReadService = new FundLedgerReadService(workspaceService, fundContext, fundAccountReadService);
                var cashFinancingReadService = new CashFinancingReadService(workspaceService, fundAccountReadService);
                var reconciliationRepository = new InMemoryReconciliationRunRepository();
                var strategyReconciliationService = new ReconciliationRunService(
                    runReadService: runReadService,
                    projectionService: new ReconciliationProjectionService(),
                    repository: reconciliationRepository);
                var reconciliationReadService = new ReconciliationReadService(
                    fundAccountService,
                    fundAccountReadService,
                    workspaceService,
                    strategyReconciliationService);
                var workbenchService = new FundReconciliationWorkbenchService(
                    reconciliationReadService,
                    fundAccountService,
                    workspaceService,
                    new FakeWorkstationReconciliationApiClient([], []));
                var fundOperationsWorkspaceReadService = CreateFundOperationsWorkspaceReadService(
                    fundAccountService,
                    store,
                    portfolioReadService,
                    strategyReconciliationService);

                using var viewModel = new FundLedgerViewModel(
                    fundLedgerReadService,
                    fundContext,
                    navigation,
                    fundAccountReadService,
                    cashFinancingReadService,
                    workbenchService,
                    fundOperationsWorkspaceReadService,
                    workspaceService);

                await viewModel.LoadAsync();

                viewModel.CashFlowEntries.Should().HaveCount(3);
                viewModel.CashFlowBuckets.Should().NotBeEmpty();
                viewModel.CashProjectionStatusText.Should().Contain("cash-flow event");

                var entityView = viewModel.LedgerDimensions.Single(view => view.Key == "entity-linked");
                entityView.LinkedAccountCount.Should().Be(1);
                entityView.TrialBalanceLineCount.Should().Be(3);
                entityView.JournalEntryCount.Should().Be(2);

                viewModel.SelectedLedgerDimension = entityView;

                viewModel.VisibleTrialBalance.Should().HaveCount(3);
                viewModel.VisibleTrialBalance.Should().OnlyContain(line => line.FinancialAccountId == accountId.ToString());
                viewModel.VisibleJournal.Should().HaveCount(2);
                viewModel.SelectedLedgerJournalEntriesText.Should().Be("2");
                viewModel.SelectedLedgerTrialBalanceLinesText.Should().Be("3");
                viewModel.SelectedLedgerAssetBalanceText.Should().NotBe("-");

                var consolidatedView = viewModel.LedgerDimensions.Single(view => view.Key == "consolidated");
                viewModel.SelectedLedgerDimension = consolidatedView;

                viewModel.SelectedLedgerDimensionDisplayText.Should().Be("Consolidated Fund View");
                viewModel.VisibleTrialBalance.Should().HaveCount(viewModel.TrialBalance.Count);
                viewModel.VisibleJournal.Should().HaveCount(viewModel.Journal.Count);

                viewModel.SelectedLedgerDimension = entityView;

                viewModel.SelectedLedgerDimensionDisplayText.Should().Be(entityView.DisplayName);
                viewModel.VisibleTrialBalance.Should().OnlyContain(line => line.FinancialAccountId == accountId.ToString());
                viewModel.VisibleJournal.Should().OnlyContain(line =>
                    line.FinancialAccountIds != null &&
                    line.FinancialAccountIds.Contains(accountId.ToString(), StringComparer.OrdinalIgnoreCase));
            }
            finally
            {
                if (File.Exists(storagePath))
                {
                    File.Delete(storagePath);
                }
            }
        });
    }

    private static Guid ToFundId(string fundProfileId)
        => new(MD5.HashData(Encoding.UTF8.GetBytes(fundProfileId.Trim())));

    private static FundOperationsWorkspaceReadService CreateFundOperationsWorkspaceReadService(
        IFundAccountService fundAccountService,
        IStrategyRepository strategyRepository,
        PortfolioReadService portfolioReadService,
        IReconciliationRunService strategyReconciliationService,
        IOperationsContinuityWorkflowService? operationsContinuityWorkflowService = null)
    {
        var securityMasterQuery = new StubApplicationSecurityMasterQueryService();
        return new FundOperationsWorkspaceReadService(
            fundAccountService,
            strategyRepository,
            portfolioReadService,
            new NavAttributionService(securityMasterQuery),
            new ReportGenerationService(securityMasterQuery),
            strategyReconciliationService: strategyReconciliationService,
            operationsContinuityWorkflowService: operationsContinuityWorkflowService);
    }

    private static OperationsContinuityWorkflowService CreateOperationsContinuityWorkflowService()
    {
        var derivation = new OperationsStatusDerivationService();
        return new OperationsContinuityWorkflowService(
            new InMemoryOperationsContinuityRepository(derivation),
            new InMemoryOperationsWorkflowAuditStore(),
            derivation);
    }

    private static PrivateCapitalCloseCockpitDto BuildPrivateCapitalCloseCockpit()
    {
        var workflowId = Guid.Parse("aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb");
        var fundAccountId = Guid.Parse("bbbbbbbb-1111-2222-3333-cccccccccccc");
        var projectedAt = new DateTimeOffset(2026, 6, 30, 21, 0, 0, TimeSpan.Zero);
        var navEvidence = CloseEvidence("shadow-nav-pack", "Shadow NAV support package", "/evidence/shadow-nav-pack");
        var approvalEvidence = CloseEvidence("approval-close", "Approval close evidence", "/evidence/approval-close");

        return new PrivateCapitalCloseCockpitDto(
            FundProfileId: "alpha-fund",
            LedgerBookId: Guid.Parse("cccccccc-1111-2222-3333-dddddddddddd"),
            FundAccountId: fundAccountId,
            PeriodId: "2026-06",
            EntityId: "entity-alpha",
            ProjectedAtUtc: projectedAt,
            CockpitRoute: "/api/workstation/operations/private-capital-close-cockpit?fundProfileId=alpha-fund",
            OverallStatus: EvidenceStatusDto.ReviewRequired,
            IsReadyToClose: false,
            ReadinessScore: 78,
            WorkflowCount: 1,
            FundEventCount: 3,
            CapitalAccountCount: 2,
            ReportOutputCount: 2,
            DeliveredReportOutputCount: 1,
            ReadyLaneCount: 1,
            BlockedLaneCount: 0,
            Lanes:
            [
                new PrivateCapitalCloseCockpitLaneDto(
                    "partner-capital-tie-out",
                    "Partner capital tie-out",
                    EvidenceStatusDto.ReviewRequired,
                    false,
                    "LP capital statement needs controller approval before close sign-off.",
                    "OperationsClose",
                    1,
                    [CloseEvidence("capital-tie-out", "Capital tie-out evidence", "/evidence/capital-tie-out")],
                    ["Approve partner capital tie-out"]),
                new PrivateCapitalCloseCockpitLaneDto(
                    "nav-support",
                    "NAV support package",
                    EvidenceStatusDto.Ready,
                    true,
                    "Shadow NAV package retained with pricing, cash, and position evidence.",
                    "FundReportPack",
                    1,
                    [navEvidence])
            ],
            Workflows:
            [
                new PrivateCapitalCloseCockpitWorkflowDto(
                    workflowId,
                    fundAccountId,
                    "2026-06",
                    OperationsWorkflowStatusDto.ApprovalPending,
                    78,
                    false,
                    "OperationsClose",
                    "close-package-alpha-2026-06",
                    "FundReportPack",
                    1,
                    1,
                    projectedAt)
            ],
            Blockers:
            [
                new OperationsCloseReadinessBlockerDto(
                    "CAPITAL_TIE_OUT_APPROVAL",
                    "Approvals",
                    "warning",
                    "Controller approval required for partner capital tie-out.",
                    OperationsGateKeyDto.Approval,
                    "OperationsClose")
            ],
            NextActions:
            [
                new OperationsNextActionDto(
                    "approve-capital-tie-out",
                    "Approve partner capital tie-out",
                    "OperationsClose",
                    OperationsGateKeyDto.Approval)
            ],
            LiveCapabilities: ["Approval history", "NAV support packages"],
            PlannedCapabilities: ["Administrator-versus-Meridian shadow NAV tie-out automation"],
            ApprovalHistory:
            [
                new PrivateCapitalCloseCockpitApprovalDto(
                    "approval-close-alpha",
                    workflowId,
                    fundAccountId,
                    "2026-06",
                    OperationsApprovalStateDto.Approved,
                    "fund-ops",
                    "controller",
                    "Close package approved after partner capital review.",
                    projectedAt.AddMinutes(-30),
                    projectedAt.AddMinutes(-10),
                    "OperationsClose",
                    1,
                    [approvalEvidence])
            ],
            NavSupportPackages:
            [
                new PrivateCapitalNavSupportPackageDto(
                    "nav-pack-alpha",
                    "NAV support package",
                    EvidenceStatusDto.Ready,
                    true,
                    "Shadow NAV package retained with pricing, cash, and position evidence.",
                    "FundReportPack",
                    12_345.67m,
                    "USD",
                    1,
                    [navEvidence],
                    [new PrivateCapitalNavSupportComponentDto("positions", "Positions", EvidenceStatusDto.Ready, true, "Positions retained.", "OperationsClose", 100)])
            ]);
    }

    private static OperationsEvidenceLinkDto CloseEvidence(string evidenceId, string label, string route)
        => new(evidenceId, label, route, "test", new DateTimeOffset(2026, 6, 30, 21, 0, 0, TimeSpan.Zero));

    private sealed class StubPrivateCapitalCloseCockpitService : IPrivateCapitalCloseCockpitService
    {
        private readonly PrivateCapitalCloseCockpitDto _cockpit;

        public StubPrivateCapitalCloseCockpitService(PrivateCapitalCloseCockpitDto cockpit)
        {
            _cockpit = cockpit;
        }

        public string? RequestedFundProfileId { get; private set; }

        public Task<PrivateCapitalCloseCockpitDto> GetCockpitAsync(
            string? fundProfileId = null,
            Guid? ledgerBookId = null,
            Guid? fundAccountId = null,
            string? periodId = null,
            string? entityId = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            RequestedFundProfileId = fundProfileId;
            return Task.FromResult(_cockpit);
        }
    }

    private static async Task WaitForConditionAsync(Func<bool> condition, int attempts = 40)
    {
        for (var index = 0; index < attempts; index++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(25);
        }

        condition().Should().BeTrue();
    }

    private static ReconciliationBreakQueueItem BuildBreakQueueItem(string runId)
        => new(
            BreakId: $"{runId}:price-gap",
            RunId: runId,
            StrategyName: "Reconciliation Strategy",
            Category: ReconciliationBreakCategory.AmountMismatch,
            Status: ReconciliationBreakQueueStatus.Open,
            Variance: 12.5m,
            Reason: "Ledger and portfolio quantities differ.",
            AssignedTo: null,
            DetectedAt: new DateTimeOffset(2026, 3, 21, 16, 35, 0, TimeSpan.Zero),
            LastUpdatedAt: new DateTimeOffset(2026, 3, 21, 16, 35, 0, TimeSpan.Zero),
            Severity: ReconciliationBreakSeverity.High,
            ExceptionRoute: "fund-ops-review",
            ToleranceProfileId: "price-variance-ops",
            ToleranceBand: 100m,
            RequiredSignoffRole: "Fund operations lead",
            SignoffStatus: "pending-signoff",
            BreakExplanation: new ReconciliationBreakExplanationDto(
                Summary: "Broker statement market value break from custodian row 9.",
                SourceSystems: ["broker", "Meridian ledger"],
                ProbableCause: "Broker statement market value could not be tied to the retained ledger posting.",
                LedgerImpact: "Securities value and cash close readiness remain blocked by USD 12.50.",
                SuggestedNextAction: "Attach broker statement evidence, review the ledger posting, and route to Fund operations lead.",
                EvidenceLinks: ["/api/workstation/reconciliation/statement-runs/run-fund-ops#row-9", "statement-hash:price-gap"]));

    private static ReconciliationRunDetail BuildStrategyDetail(string runId)
        => new(
            Summary: new ReconciliationRunSummary(
                ReconciliationRunId: "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                RunId: runId,
                CreatedAt: new DateTimeOffset(2026, 3, 21, 16, 40, 0, TimeSpan.Zero),
                PortfolioAsOf: new DateTimeOffset(2026, 3, 21, 16, 30, 0, TimeSpan.Zero),
                LedgerAsOf: new DateTimeOffset(2026, 3, 21, 16, 31, 0, TimeSpan.Zero),
                MatchCount: 2,
                BreakCount: 1,
                OpenBreakCount: 1,
                HasTimingDrift: false,
                AmountTolerance: 0.01m,
                MaxAsOfDriftMinutes: 5,
                SecurityIssueCount: 1,
                HasSecurityCoverageIssues: true),
            Matches:
            [
                new ReconciliationMatchDto(
                    CheckId: "cash-balance",
                    Label: "Cash balance",
                    ExpectedSource: "Ledger",
                    ActualSource: "Portfolio",
                    ExpectedAmount: 750m,
                    ActualAmount: 750m,
                    Variance: 0m,
                    ExpectedAsOf: new DateTimeOffset(2026, 3, 21, 16, 30, 0, TimeSpan.Zero),
                    ActualAsOf: new DateTimeOffset(2026, 3, 21, 16, 30, 0, TimeSpan.Zero))
            ],
            Breaks:
            [
                new ReconciliationBreakDto(
                    CheckId: "price-gap",
                    Label: "TSLA market value",
                    Category: ReconciliationBreakCategory.AmountMismatch,
                    Status: ReconciliationBreakStatus.Open,
                    MissingSource: "Portfolio",
                    ExpectedAmount: 150m,
                    ActualAmount: 137.5m,
                    Variance: 12.5m,
                    Severity: ReconciliationBreakSeverity.High,
                    Reason: "Broker statement has not been normalized yet.",
                    ExpectedAsOf: new DateTimeOffset(2026, 3, 21, 16, 30, 0, TimeSpan.Zero),
                    ActualAsOf: new DateTimeOffset(2026, 3, 21, 16, 29, 0, TimeSpan.Zero))
            ],
            SecurityCoverageIssues:
            [
                new ReconciliationSecurityCoverageIssueDto(
                    Source: "Portfolio",
                    Symbol: "TSLA",
                    AccountName: "Primary Custody",
                    Reason: "Security Master coverage is missing.")
            ]);

    private static StrategyRunEntry BuildFundScopedRun(string runId, string? financialAccountId = null)
    {
        var startedAt = new DateTimeOffset(2026, 3, 21, 16, 0, 0, TimeSpan.Zero);
        var completedAt = startedAt.AddMinutes(30);
        var cashFlows = new CashFlowEntry[]
        {
            new TradeCashFlow(startedAt.AddMinutes(1), 500m, "AAPL", 10, 50m),
            new CommissionCashFlow(startedAt.AddMinutes(1), -1m, "AAPL", Guid.NewGuid()),
            new DividendCashFlow(startedAt.AddDays(5), 20m, "MSFT", 100, 0.20m)
        };
        var positions = new Dictionary<string, Position>(StringComparer.OrdinalIgnoreCase)
        {
            ["AAPL"] = new("AAPL", 10, 40m, 0m, 0m),
            ["TSLA"] = new("TSLA", -5, 30m, 0m, 0m)
        };
        var accountSnapshot = new FinancialAccountSnapshot(
            AccountId: BacktestDefaults.DefaultBrokerageAccountId,
            DisplayName: "Primary Brokerage",
            Kind: FinancialAccountKind.Brokerage,
            Institution: "Simulated Broker",
            Cash: 750m,
            MarginBalance: 0m,
            LongMarketValue: 400m,
            ShortMarketValue: -150m,
            Equity: 1_000m,
            Positions: positions,
            Rules: new FinancialAccountRules());
        var snapshot = new PortfolioSnapshot(
            Timestamp: completedAt,
            Date: DateOnly.FromDateTime(completedAt.UtcDateTime),
            Cash: 750m,
            MarginBalance: 0m,
            LongMarketValue: 400m,
            ShortMarketValue: -150m,
            TotalEquity: 1_000m,
            DailyReturn: 0m,
            Positions: positions,
            Accounts: new Dictionary<string, FinancialAccountSnapshot>(StringComparer.OrdinalIgnoreCase)
            {
                [accountSnapshot.AccountId] = accountSnapshot
            },
            DayCashFlows: cashFlows);

        var request = new BacktestRequest(
            From: new DateOnly(2026, 3, 20),
            To: new DateOnly(2026, 3, 21),
            Symbols: ["AAPL", "TSLA"],
            InitialCash: 1_000m,
            DataRoot: "./data");
        var metrics = new BacktestMetrics(
            InitialCapital: 1_000m,
            FinalEquity: 1_000m,
            GrossPnl: 0m,
            NetPnl: 0m,
            TotalReturn: 0m,
            AnnualizedReturn: 0m,
            SharpeRatio: 0d,
            SortinoRatio: 0d,
            CalmarRatio: 0d,
            MaxDrawdown: 0m,
            MaxDrawdownPercent: 0m,
            MaxDrawdownRecoveryDays: 0,
            ProfitFactor: 1d,
            WinRate: 1d,
            TotalTrades: 0,
            WinningTrades: 0,
            LosingTrades: 0,
            TotalCommissions: 0m,
            TotalMarginInterest: 0m,
            TotalShortRebates: 0m,
            Xirr: 0d,
            SymbolAttribution: new Dictionary<string, SymbolAttribution>());
        var result = new BacktestResult(
            Request: request,
            Universe: new HashSet<string>(["AAPL", "TSLA"], StringComparer.OrdinalIgnoreCase),
            Snapshots: [snapshot],
            CashFlows: cashFlows,
            Fills: [],
            Metrics: metrics,
            Ledger: CreateLedger(financialAccountId),
            ElapsedTime: TimeSpan.FromMinutes(30),
            TotalEventsProcessed: 100);

        return StrategyRunEntry.Start("recon-strategy", "Reconciliation Strategy", RunType.Backtest) with
        {
            RunId = runId,
            StartedAt = startedAt,
            EndedAt = completedAt,
            Metrics = result,
            DatasetReference = "dataset/us/equities",
            FeedReference = "synthetic:equities",
            PortfolioId = "recon-portfolio",
            LedgerReference = "recon-ledger",
            AuditReference = $"audit-{runId}",
            FundProfileId = "alpha-fund",
            FundDisplayName = "Alpha Fund"
        };
    }

    private static IReadOnlyLedger CreateLedger(string? financialAccountId = null)
    {
        var ledger = new global::Meridian.Ledger.Ledger();
        PostBalancedEntry(ledger, new DateTimeOffset(2026, 3, 21, 16, 0, 0, TimeSpan.Zero), "Initial capital",
        [
            (LedgerAccounts.Cash, 1_000m, 0m),
            (LedgerAccounts.CapitalAccount, 0m, 1_000m)
        ]);
        PostBalancedEntry(ledger, new DateTimeOffset(2026, 3, 21, 16, 10, 0, 0, TimeSpan.Zero), "Buy AAPL",
        [
            (LedgerAccounts.Securities("AAPL", financialAccountId), 400m, 0m),
            (financialAccountId is null ? LedgerAccounts.Cash : LedgerAccounts.CashAccount(financialAccountId), 0m, 400m)
        ]);
        PostBalancedEntry(ledger, new DateTimeOffset(2026, 3, 21, 16, 20, 0, 0, TimeSpan.Zero), "Open TSLA short",
        [
            (financialAccountId is null ? LedgerAccounts.Cash : LedgerAccounts.CashAccount(financialAccountId), 150m, 0m),
            (LedgerAccounts.ShortSecuritiesPayable("TSLA", financialAccountId), 0m, 150m)
        ]);
        return ledger;
    }

    private static void PostBalancedEntry(
        global::Meridian.Ledger.Ledger ledger,
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

    private sealed class StubSecurityReferenceLookup : ISecurityReferenceLookup
    {
        private readonly Dictionary<string, WorkstationSecurityReference> _references = new(StringComparer.OrdinalIgnoreCase);

        public void Register(string symbol, WorkstationSecurityReference reference)
        {
            _references[symbol] = reference;
        }

        public Task<WorkstationSecurityReference?> GetBySymbolAsync(string symbol, CancellationToken ct = default)
        {
            _references.TryGetValue(symbol, out var reference);
            return Task.FromResult<WorkstationSecurityReference?>(reference);
        }
    }

    private sealed class StubApplicationSecurityMasterQueryService :
        AppSecurityMasterQueryService,
        Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService
    {
        public Task<SecurityDetailDto?> GetByIdAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult<SecurityDetailDto?>(null);

        public Task<SecurityDetailDto?> GetByIdentifierAsync(SecurityIdentifierKind identifierKind, string identifierValue, string? provider, CancellationToken ct = default, DateTimeOffset? asOfUtc = null)
        {
            if (string.Equals(identifierValue, "AAPL", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult<SecurityDetailDto?>(new SecurityDetailDto(
                    SecurityId: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                    AssetClass: "Equity",
                    Status: SecurityStatusDto.Active,
                    DisplayName: "Apple Inc.",
                    Currency: "USD",
                    CommonTerms: JsonDocument.Parse("{}").RootElement.Clone(),
                    AssetSpecificTerms: JsonDocument.Parse("{}").RootElement.Clone(),
                    Identifiers:
                    [
                        new SecurityIdentifierDto(
                            Kind: SecurityIdentifierKind.Ticker,
                            Value: "AAPL",
                            IsPrimary: true,
                            ValidFrom: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                            Provider: "test")
                    ],
                    Aliases: [],
                    Version: 1,
                    EffectiveFrom: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                    EffectiveTo: null));
            }

            return Task.FromResult<SecurityDetailDto?>(null);
        }

        public Task<IReadOnlyList<SecuritySummaryDto>> SearchAsync(SecuritySearchRequest request, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SecuritySummaryDto>>([]);

        public Task<IReadOnlyList<SecurityMasterEventEnvelope>> GetHistoryAsync(SecurityHistoryRequest request, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SecurityMasterEventEnvelope>>([]);

        public Task<SecurityEconomicDefinitionRecord?> GetEconomicDefinitionByIdAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult<SecurityEconomicDefinitionRecord?>(null);

        public Task<TradingParametersDto?> GetTradingParametersAsync(Guid securityId, DateTimeOffset asOf, CancellationToken ct = default)
            => Task.FromResult<TradingParametersDto?>(null);

        public Task<IReadOnlyList<CorporateActionDto>> GetCorporateActionsAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CorporateActionDto>>([]);

        public Task<PreferredEquityTermsDto?> GetPreferredEquityTermsAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult<PreferredEquityTermsDto?>(null);

        public Task<ConvertibleEquityTermsDto?> GetConvertibleEquityTermsAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult<ConvertibleEquityTermsDto?>(null);
    }
}
#endif
