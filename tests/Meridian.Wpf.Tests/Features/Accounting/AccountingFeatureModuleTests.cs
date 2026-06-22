using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.DataIntegration.AccountingSystem.QuickBooks;
using Meridian.FinancialOperations.AccountingClose;
using Meridian.FinancialOperations.AccountingSystem;
using Meridian.FinancialOperations.Ledger;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.FinancialOperations.PrivateCapital;
using Meridian.ProviderSdk.AccountingSystem;
using Meridian.Ui.Services.Services.Accounting;
using Meridian.Ui.Shared.Services;
using Meridian.Wpf.Features.Accounting;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;
using Meridian.Wpf.ViewModels.Accounting;
using Meridian.Wpf.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Wpf.Tests.Features.Accounting;

public sealed class AccountingFeatureModuleTests
{
    [Fact]
    public void DescribePages_ReturnsExpectedPageTagsWithoutDuplicates()
    {
        DesktopFeatureModuleTestAssertions.AssertPageTags(
            new AccountingFeatureModule(),
            "AccountingShell",
            "FundStructureSetup",
            "FundLedger",
            "RunLedger",
            "RunCashFlow",
            "FundBanking",
            "FundCashFinancing",
            "FundAccountingConfigure",
            "FundTrialBalance",
            "LedgerExplorer",
            "FundReconciliation",
            "FundAuditTrail",
            "SecurityInstrumentExplorer");
    }

    [Fact]
    public void DescribeWorkspace_ReturnsAccountingWorkspaceShell()
    {
        var workspace = DesktopFeatureModuleTestAssertions.AssertWorkspace(new AccountingFeatureModule(), "accounting", "AccountingShell");

        workspace.ShellDefinition.ViewModelType.Should().Be(typeof(AccountingWorkspaceShellViewModel));
        workspace.ShellDefinition.StateProviderType.Should().Be(typeof(AccountingWorkspaceShellStateProvider));
    }

    [Fact]
    public void DeclareCapabilities_ReturnsNoFeatureCapabilityKeys()
    {
        DesktopFeatureModuleTestAssertions.AssertNoCapabilities(new AccountingFeatureModule());
    }

    [Fact]
    public void Register_AddsAccountingViewModelsPagesAndServicesWithIntendedLifetimes()
    {
        var services = new ServiceCollection();

        new AccountingFeatureModule().Register(services);

        DesktopFeatureModuleTestAssertions.AssertRegistered<AccountingWorkspaceShellStateProvider>(services, ServiceLifetime.Transient);
        DesktopFeatureModuleTestAssertions.AssertRegistered<AccountingWorkspaceShellViewModel>(services, ServiceLifetime.Transient);
        DesktopFeatureModuleTestAssertions.AssertRegistered<AccountingWorkspaceShellPage>(services, ServiceLifetime.Transient);
        DesktopFeatureModuleTestAssertions.AssertRegistered<AccountingConfigureViewModel>(services, ServiceLifetime.Transient);
        DesktopFeatureModuleTestAssertions.AssertRegistered<AccountingConfigurePage>(services, ServiceLifetime.Transient);
        DesktopFeatureModuleTestAssertions.AssertRegistered<AccountingCloseViewModel>(services, ServiceLifetime.Transient);
        DesktopFeatureModuleTestAssertions.AssertRegistered<AccountingPostingService>(services, ServiceLifetime.Singleton);
        DesktopFeatureModuleTestAssertions.AssertRegistered<TrialBalanceProjectionService>(services, ServiceLifetime.Singleton);
        DesktopFeatureModuleTestAssertions.AssertRegistered<MonthEndCloseStateMachine>(services, ServiceLifetime.Singleton);
        DesktopFeatureModuleTestAssertions.AssertRegistered<IAccountingProjectionQueryService, AccountingProjectionQueryService>(services, ServiceLifetime.Singleton);
        DesktopFeatureModuleTestAssertions.AssertRegistered<FileAccountingConfigurationStore>(services, ServiceLifetime.Singleton);
        DesktopFeatureModuleTestAssertions.AssertRegistered<IAccountingConfigurationService, AccountingConfigurationService>(services, ServiceLifetime.Singleton);
        DesktopFeatureModuleTestAssertions.AssertRegistered<IManualJournalEntryDraftStore>(services, ServiceLifetime.Singleton);
        DesktopFeatureModuleTestAssertions.AssertRegistered<IManualJournalEntryWorkbenchService>(services, ServiceLifetime.Singleton);
        DesktopFeatureModuleTestAssertions.AssertRegistered<ICapitalAccountWorkbenchService>(services, ServiceLifetime.Singleton);
        DesktopFeatureModuleTestAssertions.AssertRegistered<IPrivateCapitalCloseCockpitService>(services, ServiceLifetime.Singleton);
        DesktopFeatureModuleTestAssertions.AssertRegistered<IAccountingPolicyService, AccountingPolicyService>(services, ServiceLifetime.Singleton);
        DesktopFeatureModuleTestAssertions.AssertRegistered<IAccountingBasisProjectionService, AccountingBasisProjectionService>(services, ServiceLifetime.Singleton);
        DesktopFeatureModuleTestAssertions.AssertRegistered<IAccountingJournalDraftService, AccountingJournalDraftService>(services, ServiceLifetime.Singleton);
        DesktopFeatureModuleTestAssertions.AssertRegistered<IAccountingPostingCandidateService, AccountingPostingCandidateService>(services, ServiceLifetime.Singleton);
        DesktopFeatureModuleTestAssertions.AssertRegistered<IAccountingPostingCandidateWriteBuilder, AccountingPostingCandidateService>(services, ServiceLifetime.Singleton);
        DesktopFeatureModuleTestAssertions.AssertRegistered<IAccountingPostingCandidatePostService>(services, ServiceLifetime.Singleton);
        DesktopFeatureModuleTestAssertions.AssertRegistered<IAccountingBasisProjectionSetService, AccountingBasisProjectionSetService>(services, ServiceLifetime.Singleton);
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IAccountingSystemProvider) && descriptor.ImplementationType == typeof(QuickBooksFixtureAccountingProvider) && descriptor.Lifetime == ServiceLifetime.Singleton);
        DesktopFeatureModuleTestAssertions.AssertRegistered<AccountingSystemIntegrationService>(services, ServiceLifetime.Singleton);
        DesktopFeatureModuleTestAssertions.AssertRegistered<AccountingProductionReadinessService>(services, ServiceLifetime.Singleton);
    }

    [Theory]
    [InlineData("AccountingShell", "AccountingShell", "accounting")]
    [InlineData("GovernanceShell", "AccountingShell", "accounting")]
    [InlineData("AccountingWorkspace", "AccountingShell", "accounting")]
    [InlineData("OperationsContinuity", "FundLedger", "accounting")]
    [InlineData("OperationsClose", "FundLedger", "accounting")]
    [InlineData("EvidenceWorkbench", "FundAuditTrail", "accounting")]
    [InlineData("AccountingApprovals", "FundAuditTrail", "accounting")]
    [InlineData("LedgerInspector", "RunLedger", "accounting")]
    public void ShellRegistry_ResolvesAccountingAliasesAndRootNavigationTags(string requestedTag, string canonicalTag, string workspaceId)
    {
        DesktopFeatureModuleTestAssertions.AssertRouteResolves(requestedTag, canonicalTag, workspaceId);
    }
}
