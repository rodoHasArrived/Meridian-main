using System;
using System.IO;
using Meridian.FinancialOperations.AccountingClose;
using Meridian.DataIntegration.AccountingSystem.QuickBooks;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.AccountingSystem;
using Meridian.FinancialOperations.Ledger;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.FinancialOperations.PrivateCapital;
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
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAccountingSystemProvider, QuickBooksFixtureAccountingProvider>());
        services.TryAddSingleton<AccountingSystemIntegrationService>();
        services.AddTransient<AccountingConfigureViewModel>();
        services.AddTransient<AccountingConfigurePage>();
        services.AddTransient<AccountingCloseViewModel>();
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
                var config = configService.LoadConfigAsync().GetAwaiter().GetResult();
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
