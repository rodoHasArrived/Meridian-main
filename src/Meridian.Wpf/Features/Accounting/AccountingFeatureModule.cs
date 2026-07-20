using System;
using System.IO;
using System.Threading.Tasks;
using Meridian.Application.Accounting;
using Meridian.Application.FundStructure;
using Meridian.Contracts.Catalog;
using Meridian.Contracts.Domain;
using Meridian.FinancialOperations.AccountingClose;
using Meridian.DataIntegration.AccountingSystem.Fixtures;
using Meridian.DataIntegration.AccountingSystem.QuickBooks;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Services;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.AccountingSystem;
using Meridian.FinancialOperations.Ledger;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.FinancialOperations.PrivateCapital;
using Meridian.Instruments.AssetOperations;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.PortfolioRecords.Accounts;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.ProviderSdk.AccountingSystem;
using Meridian.Ui.Services.Services.Accounting;
using Meridian.Ui.Shared.Services;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;
using Meridian.Wpf.ViewModels.Accounting;
using Meridian.Wpf.Views;
using Meridian.Storage.Ledger;
using Meridian.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Meridian.Wpf.Features.Accounting;

public sealed class AccountingFeatureModule : IDesktopFeatureModule
{
    private static readonly WorkspaceCapabilityDescriptor Capability = ShellNavigationCatalog.BuildAccountingCapability();

    public void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTransient<AccountingWorkspaceShellStateProvider>();
        services.AddTransient<AccountingWorkspaceShellViewModel>();
        services.AddTransient<AccountingWorkspaceShellPage>();
        services.AddSingleton(sp => new InMemoryFundAccountService(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Meridian",
                "fund-accounts.json")));
        services.AddSingleton<IFundAccountService>(sp => sp.GetRequiredService<InMemoryFundAccountService>());
        services.AddSingleton<IFundStructureService>(sp => new InMemoryFundStructureService(
            sp.GetRequiredService<IFundAccountService>(),
            sharedDataAccessService: null,
            securityMasterQueryService: sp.GetService<Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService>(),
            persistencePath: Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Meridian",
                "fund-structure.json")));
        services.AddSingleton<FundStructureSetupWorkflowService>();
        services.AddSingleton<FundAccountReadService>();
        services.AddSingleton<FundLedgerReadService>();
        services.AddSingleton<ReconciliationReadService>();
        services.AddSingleton<CashFinancingReadService>();
        services.AddSingleton<IWorkstationReconciliationApiClient, WorkstationReconciliationApiClient>();
        services.AddSingleton<IWorkstationSecurityMasterApiClient, WorkstationSecurityMasterApiClient>();
        // Operator accounting-configuration traffic goes through the server's governed HTTP
        // surface (audit finding P8) so the server stays the single writer and stamps the
        // session actor; the in-process IAccountingConfigurationService below remains for
        // background workers that have not yet migrated.
        services.AddSingleton<IWorkstationAccountingApiClient, WorkstationAccountingApiClient>();
        // Passport Workbench governed-write editor (Phase 4 desktop parity).
        services.AddTransient<Meridian.Wpf.ViewModels.SecurityPassportEditorViewModel>();
        services.AddTransient<Meridian.Wpf.Views.SecurityPassportEditorPage>();
        services.AddSingleton<ISecurityAssetProfileWorkflowClient, SecurityAssetProfileWorkflowClient>();
        services.AddSingleton<IOperationsControlCenterClient, OperationsControlCenterClient>();
        services.AddSingleton<IFundReconciliationWorkbenchService, FundReconciliationWorkbenchService>();
        services.AddSingleton<IStatementReconciliationWorkbenchService, StatementReconciliationWorkbenchService>();
        services.TryAddSingleton<AccountingPostingService>();
        services.TryAddSingleton<TrialBalanceProjectionService>();
        services.TryAddSingleton<MonthEndCloseStateMachine>();
        services.TryAddSingleton<CloseEvidencePackageService>();
        services.TryAddSingleton<IAccountingProjectionQueryService, AccountingProjectionQueryService>();
        services.TryAddSingleton<FileAccountingConfigurationStore>(sp =>
            new FileAccountingConfigurationStore(
                Path.Combine(ResolveAccountingDataDirectory(sp), "accounting-configuration.json")));
        services.TryAddSingleton<IAccountingConfigurationStore>(sp => sp.GetRequiredService<FileAccountingConfigurationStore>());
        services.TryAddSingleton<IAccountingActionAuditStore>(sp =>
            sp.GetRequiredService<IAccountingConfigurationStore>() is IAccountingActionAuditStore auditStore
                ? auditStore
                : sp.GetRequiredService<FileAccountingConfigurationStore>());
        services.TryAddSingleton<IAccountingConfigurationService, AccountingConfigurationService>();
        services.TryAddSingleton<IManualJournalEntryDraftStore>(sp =>
            new FileManualJournalEntryDraftStore(
                Path.Combine(ResolveAccountingDataDirectory(sp), "manual-journal-drafts.json")));
        services.TryAddSingleton<FileDailyValuationPortfolioSource>(sp =>
            new FileDailyValuationPortfolioSource(
                Path.Combine(ResolveAccountingDataDirectory(sp), "daily-valuation-schedules.json")));
        services.TryAddSingleton<IDailyValuationPortfolioSource>(sp =>
            sp.GetRequiredService<FileDailyValuationPortfolioSource>());
        services.TryAddSingleton<IDailyValuationScheduleStatusSource>(sp =>
            sp.GetRequiredService<FileDailyValuationPortfolioSource>());
        services.TryAddSingleton<DailyValuationPositionService>(sp =>
            new DailyValuationPositionService(
                sp.GetService<IPositionSnapshotStore>(),
                sp.GetService<ICanonicalSymbolRegistry>(),
                sp.GetService<Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService>()));
        services.TryAddSingleton<FileAutomatedJournalScheduleStore>(sp =>
            new FileAutomatedJournalScheduleStore(
                Path.Combine(ResolveAccountingDataDirectory(sp), "monthly-automated-journal-schedules.json")));
        services.TryAddSingleton<IAutomatedJournalScheduleStore>(sp =>
            sp.GetRequiredService<FileAutomatedJournalScheduleStore>());
        services.TryAddSingleton<IAutomatedJournalScheduleStatusSource>(sp =>
            sp.GetRequiredService<FileAutomatedJournalScheduleStore>());
        services.TryAddSingleton<IAccountingPositionSnapshotCaptureService>(sp =>
            new AccountingPositionSnapshotCaptureService(
                sp.GetService<IPositionSnapshotStore>(),
                sp.GetService<IDailyValuationPortfolioSource>(),
                sp.GetService<IAutomatedJournalScheduleStore>(),
                sp.GetService<IAccountQueryService>(),
                sp.GetService<ILedgerJournalStore>(),
                sp.GetService<IFundProfileTenancyRegistry>()));
        services.TryAddSingleton<IAutomatedJournalDividendPositionResolver>(sp =>
            new PositionSnapshotAutomatedJournalDividendPositionResolver(
                sp.GetService<IPositionSnapshotStore>()));
        services.TryAddSingleton<IAccountingReportPackageService>(sp =>
            new AccountingReportPackageService(
                sp.GetService<IAccountingCloseManagementService>(),
                new StorageOptions
                {
                    RootPath = Path.GetDirectoryName(ResolveAccountingDataDirectory(sp))
                        ?? ResolveAccountingDataDirectory(sp)
                }));
        services.TryAddSingleton<IAutomatedJournalCapitalAccountReconciliationResolver>(sp =>
            new LedgerCapitalAccountReconciliationResolver(
                sp.GetService<ILedgerJournalStore>(),
                sp.GetService<IFundProfileTenancyRegistry>()));
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.TryAddSingleton<IManualJournalEntryWorkbenchService>(sp =>
            ActivatorUtilities.CreateInstance<ManualJournalEntryWorkbenchService>(sp));
        services.TryAddSingleton<Meridian.Contracts.Ledger.IManualJournalEntryLifecycleService>(sp =>
            (Meridian.Contracts.Ledger.IManualJournalEntryLifecycleService)sp.GetRequiredService<IManualJournalEntryWorkbenchService>());
        services.TryAddSingleton(sp =>
            new AutomatedJournalDraftIntakeService(
                sp.GetRequiredService<IManualJournalEntryWorkbenchService>(),
                sp.GetRequiredService<IManualJournalEntryDraftStore>(),
                sp.GetRequiredService<IAccountingConfigurationService>()));
        services.TryAddSingleton<DailyValuationBatchLifecycleService>();
        services.TryAddSingleton(AutomatedJournalEvidencePolicy.Default);
        services.TryAddSingleton(sp =>
        {
            var securityMaster = sp.GetService<Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService>();
            var providerRegistry = sp.GetService<ProviderRegistry>();
            var journalStore = sp.GetService<ILedgerJournalStore>();
            return new AutomatedJournalIntakeRunner(
                sp.GetRequiredService<AutomatedJournalDraftIntakeService>(),
                new FeeScheduleAccrualEventProducer(),
                securityMaster is null ? null : new CorporateActionDividendEventProducer(securityMaster),
                sp.GetService<ILedgerBookService>(),
                providerRegistry is null || journalStore is null
                    ? null
                    : new DailyMarkToMarketService(
                        new RegisteredHistoricalCloseMarkPriceSource(providerRegistry),
                        new LedgerMarkToMarketCarryingValueSource(journalStore)),
                sp.GetRequiredService<DailyValuationPositionService>(),
                sp.GetRequiredService<AutomatedJournalEvidencePolicy>(),
                sp.GetService<IAutomatedJournalCapitalAccountReconciliationResolver>(),
                sp.GetRequiredService<TimeProvider>());
        });
        services.TryAddSingleton<IAccountingClosePostingWorkbench>(sp =>
            new AccountingClosePostingWorkbenchBridge(
                sp.GetRequiredService<AutomatedJournalIntakeRunner>(),
                sp.GetRequiredService<IManualJournalEntryWorkbenchService>(),
                sp.GetRequiredService<IManualJournalEntryLifecycleService>(),
                sp.GetService<ILedgerBookService>(),
                sp.GetService<ReportingReconciliationEvidenceRetentionService>(),
                sp.GetService<IFundProfileTenancyRegistry>()));
        services.TryAddSingleton<DailyValuationScheduledWorker>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, DailyValuationSchedulerHostedService>());
        services.TryAddSingleton<AutomatedJournalScheduledWorker>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, AutomatedJournalSchedulerHostedService>());
        services.TryAddSingleton<ICapitalAccountWorkbenchService>(sp =>
            new CapitalAccountWorkbenchService(
                sp.GetRequiredService<IManualJournalEntryWorkbenchService>(),
                sp.GetService<ReportPackWorkflowService>()));
        services.TryAddSingleton<IOperationsStatusDerivationService, OperationsStatusDerivationService>();
        services.TryAddSingleton<IOperationsContinuityRepository>(sp =>
            new FileOperationsContinuityRepository(
                ResolveAccountingDataDirectory(sp),
                sp.GetRequiredService<IOperationsStatusDerivationService>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<FileOperationsContinuityRepository>>()));
        services.TryAddSingleton<IOperationsWorkflowAuditStore>(sp =>
            new FileOperationsWorkflowAuditStore(
                ResolveAccountingDataDirectory(sp),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<FileOperationsWorkflowAuditStore>>()));
        services.TryAddSingleton<IOperationsContinuityWorkflowService>(sp =>
            new OperationsContinuityWorkflowService(
                sp.GetRequiredService<IOperationsContinuityRepository>(),
                sp.GetRequiredService<IOperationsWorkflowAuditStore>(),
                sp.GetRequiredService<IOperationsStatusDerivationService>(),
                sp.GetService<ILedgerJournalStore>(),
                sp.GetService<IOperationsContinuityTransactionalCommitStore>(),
                sp.GetService<Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService>()));
        services.TryAddSingleton<IOperationsCloseCalendarService, OperationsCloseCalendarService>();
        services.TryAddSingleton<IAccountingCloseManagementService, AccountingCloseManagementService>();
        services.TryAddSingleton<IPrivateCapitalCloseCockpitService>(sp =>
            new PrivateCapitalCloseCockpitService(
                sp.GetService<IManualJournalEntryWorkbenchService>(),
                sp.GetService<IOperationsContinuityWorkflowService>(),
                sp.GetService<IDailyValuationScheduleStatusSource>(),
                sp.GetService<IAutomatedJournalScheduleStatusSource>()));
        services.TryAddSingleton<IFinancialOperationsCommandCenterReadService>(sp =>
            new FinancialOperationsCommandCenterReadService(
                sp.GetRequiredService<IOperationsContinuityWorkflowService>(),
                sp.GetService<IOperationsCloseCalendarService>(),
                sp.GetService<IPrivateCapitalCloseCockpitService>()));
        services.TryAddSingleton<IAccountingPolicyService, AccountingPolicyService>();
        services.TryAddSingleton<IAccountingBasisProjectionService, AccountingBasisProjectionService>();
        services.TryAddSingleton<IAccountingJournalDraftService, AccountingJournalDraftService>();
        services.TryAddSingleton<IFactorPaydownProjectionService, FactorPaydownProjectionService>();
        services.TryAddSingleton<IAccountingPostingCandidateService, AccountingPostingCandidateService>();
        services.TryAddSingleton<IAccountingPostingCandidateWriteBuilder, AccountingPostingCandidateService>();
        services.TryAddSingleton<IAccountingPostingCandidatePostService>(sp =>
            new AccountingPostingCandidatePostService(
                sp.GetRequiredService<IAccountingPostingCandidateWriteBuilder>(),
                sp.GetService<ILedgerJournalStore>()));
        services.TryAddSingleton<IAccountingBasisProjectionSetService, AccountingBasisProjectionSetService>();
        services.TryAddSingleton<IAccountingMigrationRunArtifactStore>(sp =>
            new FileAccountingMigrationRunArtifactStore(
                Path.Combine(ResolveAccountingDataDirectory(sp), "migration-run-artifacts.json"),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<FileAccountingMigrationRunArtifactStore>>()));
        services.TryAddSingleton<FileAccountingMigrationRunWorkerPlanStore>(sp =>
            new FileAccountingMigrationRunWorkerPlanStore(
                Path.Combine(ResolveAccountingDataDirectory(sp), "migration-run-worker-plans.json"),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<FileAccountingMigrationRunWorkerPlanStore>>()));
        services.TryAddSingleton<IAccountingMigrationRunWorkerPlanStore>(sp =>
            sp.GetRequiredService<FileAccountingMigrationRunWorkerPlanStore>());
        services.TryAddSingleton<IAccountingMigrationRunWorkerPlanWriter>(sp =>
            sp.GetRequiredService<FileAccountingMigrationRunWorkerPlanStore>());
        services.TryAddSingleton<IAccountingTenantAdministrationProfileStore>(sp =>
            new FileAccountingTenantAdministrationProfileStore(
                Path.Combine(ResolveAccountingDataDirectory(sp), "tenant-administration-profiles.json"),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<FileAccountingTenantAdministrationProfileStore>>()));
        services.TryAddSingleton<IAccountingProductionCertificationProfileStore>(sp =>
            new FileAccountingProductionCertificationProfileStore(
                Path.Combine(ResolveAccountingDataDirectory(sp), "production-certification-profiles.json"),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<FileAccountingProductionCertificationProfileStore>>()));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAccountingSystemProvider, QuickBooksFixtureAccountingProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAccountingSystemProvider, XeroFixtureAccountingProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAccountingSystemProvider, NetSuiteFixtureAccountingProvider>());
        services.TryAddSingleton<AccountingSystemIntegrationService>();
        services.TryAddSingleton<AccountingProductionReadinessService>();
        services.AddTransient<AccountingConfigureViewModel>();
        services.AddTransient<AccountingConfigurePage>();
        services.AddTransient<AccountingCloseViewModel>();
        services.AddTransient<AccountingClosePage>();
        services.AddTransient<FundStructureSetupViewModel>();
        services.AddTransient<FundAccountsViewModel>();
        services.AddTransient<FundLedgerViewModel>();
        services.AddTransient<FinancialRecordExplorerViewModel>();
        services.AddTransient<AccountPortfolioViewModel>();
    }

    public IReadOnlyList<ShellPageDescriptor> DescribePages() => Capability.Pages;

    public WorkspaceCapabilityDescriptor DescribeWorkspace() => Capability;

    private static string ResolveAccountingDataDirectory(IServiceProvider services)
    {
        var configService = services.GetService<Meridian.Wpf.Services.ConfigService>();
        if (configService is not null)
        {
            try
            {
                var config = Task.Run(() => configService.LoadConfigAsync()).GetAwaiter().GetResult();
                return Path.Combine(configService.ResolveDataRoot(config), "workstation", "accounting");
            }
            catch (Exception ex)
            {
                // Fall through to the user-local path so accounting drafts remain durable even before config initialization.
                global::Meridian.Wpf.Services.LoggingService.Instance.LogDebug(
                    "Accounting data directory resolution from config failed; using user-local path.",
                    ("exception", ex.GetType().Name),
                    ("message", ex.Message));
            }
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Meridian",
            "workstation",
            "accounting");
    }
}
