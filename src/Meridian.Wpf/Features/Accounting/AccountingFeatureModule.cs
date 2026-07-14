using System;
using System.IO;
using System.Threading.Tasks;
using Meridian.Application.FundStructure;
using Meridian.FinancialOperations.AccountingClose;
using Meridian.DataIntegration.AccountingSystem.Fixtures;
using Meridian.DataIntegration.AccountingSystem.QuickBooks;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Services;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.AccountingSystem;
using Meridian.FinancialOperations.Ledger;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.FinancialOperations.PrivateCapital;
using Meridian.Instruments.AssetOperations;
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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        services.TryAddSingleton<IManualJournalEntryWorkbenchService>(sp =>
            new ManualJournalEntryWorkbenchService(
                sp.GetRequiredService<IManualJournalEntryDraftStore>(),
                sp.GetRequiredService<IAccountingConfigurationService>(),
                sp.GetRequiredService<IAccountingActionAuditStore>(),
                sp.GetService<Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService>(),
                sp.GetService<ILedgerJournalStore>(),
                sp.GetService<ReportPackWorkflowService>()));
        services.TryAddSingleton<Meridian.Contracts.Ledger.IManualJournalEntryLifecycleService>(sp =>
            (Meridian.Contracts.Ledger.IManualJournalEntryLifecycleService)sp.GetRequiredService<IManualJournalEntryWorkbenchService>());
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
                sp.GetService<IOperationsContinuityWorkflowService>()));
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
