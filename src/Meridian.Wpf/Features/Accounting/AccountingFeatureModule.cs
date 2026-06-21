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
        services.AddSingleton<ISecurityAssetProfileWorkflowClient, SecurityAssetProfileWorkflowClient>();
        services.AddSingleton<IOperationsControlCenterClient, OperationsControlCenterClient>();
        services.AddSingleton<IFundReconciliationWorkbenchService, FundReconciliationWorkbenchService>();
        services.AddSingleton<IStatementReconciliationWorkbenchService, StatementReconciliationWorkbenchService>();
        services.TryAddSingleton<AccountingPostingService>();
        services.TryAddSingleton<TrialBalanceProjectionService>();
        services.TryAddSingleton<MonthEndCloseStateMachine>();
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
        services.TryAddSingleton<IPrivateCapitalCloseCockpitService>(sp =>
            new PrivateCapitalCloseCockpitService(
                sp.GetService<IManualJournalEntryWorkbenchService>(),
                sp.GetService<IOperationsContinuityWorkflowService>()));
        services.TryAddSingleton<IAccountingPolicyService, AccountingPolicyService>();
        services.TryAddSingleton<IAccountingBasisProjectionService, AccountingBasisProjectionService>();
        services.TryAddSingleton<IAccountingJournalDraftService, AccountingJournalDraftService>();
        services.TryAddSingleton<IAccountingPostingCandidateService, AccountingPostingCandidateService>();
        services.TryAddSingleton<IAccountingTenantAdministrationProfileStore>(sp =>
            new FileAccountingTenantAdministrationProfileStore(
                Path.Combine(ResolveAccountingDataDirectory(sp), "tenant-administration-profiles.json"),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<FileAccountingTenantAdministrationProfileStore>>()));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAccountingSystemProvider, QuickBooksFixtureAccountingProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAccountingSystemProvider, XeroFixtureAccountingProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAccountingSystemProvider, NetSuiteFixtureAccountingProvider>());
        services.TryAddSingleton<AccountingSystemIntegrationService>();
        services.TryAddSingleton<AccountingProductionReadinessService>();
        services.AddTransient<AccountingConfigureViewModel>();
        services.AddTransient<AccountingConfigurePage>();
        services.AddTransient<AccountingCloseViewModel>();
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
            catch
            {
                // Fall through to the user-local path so accounting drafts remain durable even before config initialization.
            }
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Meridian",
            "workstation",
            "accounting");
    }
}
