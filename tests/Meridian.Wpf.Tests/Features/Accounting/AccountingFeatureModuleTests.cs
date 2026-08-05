using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.DataIntegration.AccountingSystem.Fixtures;
using Meridian.DataIntegration.AccountingSystem.QuickBooks;
using Meridian.FinancialOperations.AccountingClose;
using Meridian.FinancialOperations.AccountingSystem;
using Meridian.FinancialOperations.Ledger;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.FinancialOperations.PrivateCapital;
using Meridian.ProviderSdk.AccountingSystem;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.Ledger;
using Meridian.Ui.Services.Services.Accounting;
using Meridian.Ui.Shared.Services;
using Meridian.Wpf.Features.Accounting;
using Meridian.Wpf.Services;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;
using Meridian.Wpf.ViewModels.Accounting;
using Meridian.Wpf.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
            "FundAccountingClose",
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
        DesktopFeatureModuleTestAssertions.AssertRegistered<AccountingClosePage>(services, ServiceLifetime.Transient);
        DesktopFeatureModuleTestAssertions.AssertRegistered<AccountingPostingService>(services, ServiceLifetime.Singleton);
        DesktopFeatureModuleTestAssertions.AssertRegistered<TrialBalanceProjectionService>(services, ServiceLifetime.Singleton);
        DesktopFeatureModuleTestAssertions.AssertRegistered<MonthEndCloseStateMachine>(services, ServiceLifetime.Singleton);
        DesktopFeatureModuleTestAssertions.AssertRegistered<IAccountingProjectionQueryService, AccountingProjectionQueryService>(services, ServiceLifetime.Singleton);
        DesktopFeatureModuleTestAssertions.AssertRegistered<FileAccountingConfigurationStore>(services, ServiceLifetime.Singleton);
        DesktopFeatureModuleTestAssertions.AssertRegistered<IAccountingConfigurationService, AccountingConfigurationService>(services, ServiceLifetime.Singleton);
        DesktopFeatureModuleTestAssertions.AssertRegistered<IManualJournalEntryDraftStore>(services, ServiceLifetime.Singleton);
        DesktopFeatureModuleTestAssertions.AssertRegistered<IManualJournalEntryWorkbenchService>(services, ServiceLifetime.Singleton);
        DesktopFeatureModuleTestAssertions.AssertRegistered<IDailyValuationPortfolioSource>(services, ServiceLifetime.Singleton);
        DesktopFeatureModuleTestAssertions.AssertRegistered<IDailyValuationScheduleStatusSource>(services, ServiceLifetime.Singleton);
        DesktopFeatureModuleTestAssertions.AssertRegistered<IAutomatedJournalScheduleStore>(services, ServiceLifetime.Singleton);
        DesktopFeatureModuleTestAssertions.AssertRegistered<IAutomatedJournalScheduleStatusSource>(services, ServiceLifetime.Singleton);
        DesktopFeatureModuleTestAssertions.AssertRegistered<DailyValuationPositionService>(services, ServiceLifetime.Singleton);
        DesktopFeatureModuleTestAssertions.AssertRegistered<DailyValuationBatchLifecycleService>(services, ServiceLifetime.Singleton);
        DesktopFeatureModuleTestAssertions.AssertRegistered<DailyValuationScheduledWorker>(services, ServiceLifetime.Singleton);
        DesktopFeatureModuleTestAssertions.AssertRegistered<AutomatedJournalScheduledWorker>(services, ServiceLifetime.Singleton);
        services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(IHostedService) &&
            descriptor.ImplementationType == typeof(DailyValuationSchedulerHostedService) &&
            descriptor.Lifetime == ServiceLifetime.Singleton);
        services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(IHostedService) &&
            descriptor.ImplementationType == typeof(AutomatedJournalSchedulerHostedService) &&
            descriptor.Lifetime == ServiceLifetime.Singleton);
        DesktopFeatureModuleTestAssertions.AssertRegistered<ICapitalAccountWorkbenchService>(services, ServiceLifetime.Singleton);
        DesktopFeatureModuleTestAssertions.AssertRegistered<IAccountingCloseManagementService, AccountingCloseManagementService>(services, ServiceLifetime.Singleton);
        DesktopFeatureModuleTestAssertions.AssertRegistered<IPrivateCapitalCloseCockpitService>(services, ServiceLifetime.Singleton);
        DesktopFeatureModuleTestAssertions.AssertRegistered<IAccountingPolicyService, AccountingPolicyService>(services, ServiceLifetime.Singleton);
        DesktopFeatureModuleTestAssertions.AssertRegistered<IAccountingBasisProjectionService, AccountingBasisProjectionService>(services, ServiceLifetime.Singleton);
        DesktopFeatureModuleTestAssertions.AssertRegistered<IAccountingJournalDraftService, AccountingJournalDraftService>(services, ServiceLifetime.Singleton);
        DesktopFeatureModuleTestAssertions.AssertRegistered<IAccountingPostingCandidateService, AccountingPostingCandidateService>(services, ServiceLifetime.Singleton);
        DesktopFeatureModuleTestAssertions.AssertRegistered<IAccountingPostingCandidateWriteBuilder, AccountingPostingCandidateService>(services, ServiceLifetime.Singleton);
        DesktopFeatureModuleTestAssertions.AssertRegistered<IAccountingPostingCandidateAuthorityBuilder>(services, ServiceLifetime.Singleton);
        // Registered as a graceful singleton via AssetAccountingEventSpineService.TryCreate (mirroring
        // LedgerFeatureRegistration): the WPF host does not register the asset-operations projection
        // stores, so a concrete-type registration would fail Development ValidateOnBuild. The concrete
        // resolution is asserted by Register_WiresAssetAccountingDependenciesIntoPostingService below.
        DesktopFeatureModuleTestAssertions.AssertRegistered<IAssetAccountingEventSpineService>(services, ServiceLifetime.Singleton);
        DesktopFeatureModuleTestAssertions.AssertRegistered<IAccountingPostingCandidatePostService>(services, ServiceLifetime.Singleton);
        DesktopFeatureModuleTestAssertions.AssertRegistered<IAccountingBasisProjectionSetService, AccountingBasisProjectionSetService>(services, ServiceLifetime.Singleton);
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IAccountingSystemProvider) && descriptor.ImplementationType == typeof(QuickBooksFixtureAccountingProvider) && descriptor.Lifetime == ServiceLifetime.Singleton);
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IAccountingSystemProvider) && descriptor.ImplementationType == typeof(XeroFixtureAccountingProvider) && descriptor.Lifetime == ServiceLifetime.Singleton);
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IAccountingSystemProvider) && descriptor.ImplementationType == typeof(NetSuiteFixtureAccountingProvider) && descriptor.Lifetime == ServiceLifetime.Singleton);
        DesktopFeatureModuleTestAssertions.AssertRegistered<AccountingSystemIntegrationService>(services, ServiceLifetime.Singleton);
        DesktopFeatureModuleTestAssertions.AssertRegistered<AccountingProductionReadinessService>(services, ServiceLifetime.Singleton);
    }

    [Fact]
    public void Register_WiresAssetAccountingDependenciesIntoPostingService()
    {
        var candidateBuilder = Substitute.For<
            IAccountingPostingCandidateWriteBuilder,
            IAccountingPostingCandidateAuthorityBuilder>();
        var journalStore = Substitute.For<ILedgerJournalStore>();
        var atomicTaxLotStore = Substitute.For<IAtomicTaxLotJournalStore>();
        var assetAccountingEventStore = Substitute.For<IAssetAccountingEventProjectionStore>();
        var positionStore = Substitute.For<IInstrumentPositionProjectionStore>();
        var securityMaster = Substitute.For<ISecurityMasterQueryService>();
        var ledgerBookService = Substitute.For<ILedgerBookService>();
        var accountingPolicyService = Substitute.For<IAccountingPolicyService>();
        var accountingConfigurationService = Substitute.For<IAccountingConfigurationService>();
        var services = new ServiceCollection();
        services.AddSingleton<IAccountingPostingCandidateWriteBuilder>(candidateBuilder);
        services.AddSingleton(journalStore);
        services.AddSingleton(atomicTaxLotStore);
        services.AddSingleton(assetAccountingEventStore);
        services.AddSingleton(positionStore);
        services.AddSingleton(securityMaster);
        services.AddSingleton(ledgerBookService);
        services.AddSingleton(accountingPolicyService);
        services.AddSingleton(accountingConfigurationService);

        new AccountingFeatureModule().Register(services);
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IAccountingPostingCandidateAuthorityBuilder>()
            .Should().BeSameAs(candidateBuilder);
        provider.GetRequiredService<IAssetAccountingEventSpineService>()
            .Should().BeOfType<AssetAccountingEventSpineService>();
        var postService = provider.GetRequiredService<IAccountingPostingCandidatePostService>()
            .Should().BeOfType<AccountingPostingCandidatePostService>().Subject;
        GetInjectedDependency(postService, "_journalStore").Should().BeSameAs(journalStore);
        GetInjectedDependency(postService, "_atomicTaxLotStore").Should().BeSameAs(atomicTaxLotStore);
        GetInjectedDependency(postService, "_assetAccountingEventStore").Should().BeSameAs(assetAccountingEventStore);
        GetInjectedDependency(postService, "_authorityBuilder").Should().BeSameAs(candidateBuilder);
    }

    [Fact]
    public async Task Register_ResolvesMonthlyAutomationGraph_AndHostedOneShot()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var configurationStore = new InMemoryAccountingConfigurationStore();
        var auditStore = new InMemoryAccountingActionAuditStore();
        var configurationService = new AccountingConfigurationService(configurationStore, auditStore);
        var draftStore = new InMemoryManualJournalEntryDraftStore();
        var workbench = new ManualJournalEntryWorkbenchService(draftStore, configurationService, auditStore);
        var scheduleStore = new InMemoryAutomatedJournalScheduleStore();
        services.AddSingleton<IAccountingConfigurationStore>(configurationStore);
        services.AddSingleton<IAccountingActionAuditStore>(auditStore);
        services.AddSingleton<IAccountingConfigurationService>(configurationService);
        services.AddSingleton<IManualJournalEntryDraftStore>(draftStore);
        services.AddSingleton<IManualJournalEntryWorkbenchService>(workbench);
        services.AddSingleton<IAutomatedJournalScheduleStore>(scheduleStore);
        services.AddSingleton<IAutomatedJournalScheduleStatusSource>(scheduleStore);

        new AccountingFeatureModule().Register(services);
        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<AutomatedJournalScheduledWorker>().Should().NotBeNull();
        var hosted = provider.GetServices<IHostedService>()
            .OfType<AutomatedJournalSchedulerHostedService>()
            .Should().ContainSingle().Subject;
        var result = await hosted.RunOnceAsync();

        result.Runs.Should().BeEmpty();
    }

    [Theory]
    [InlineData("AccountingShell", "AccountingShell", "accounting")]
    [InlineData("GovernanceShell", "AccountingShell", "accounting")]
    [InlineData("AccountingWorkspace", "AccountingShell", "accounting")]
    [InlineData("OperationsContinuity", "FundLedger", "accounting")]
    [InlineData("OperationsClose", "FundLedger", "accounting")]
    [InlineData("AccountingClose", "FundAccountingClose", "accounting")]
    [InlineData("CloseManagement", "FundAccountingClose", "accounting")]
    [InlineData("AccountingApprovals", "FundAuditTrail", "accounting")]
    [InlineData("LedgerInspector", "RunLedger", "accounting")]
    public void ShellRegistry_ResolvesAccountingAliasesAndRootNavigationTags(string requestedTag, string canonicalTag, string workspaceId)
    {
        DesktopFeatureModuleTestAssertions.AssertRouteResolves(requestedTag, canonicalTag, workspaceId);
    }

    [Fact]
    public void AccountingClosePage_BindsGovernedCloseManagementCommandsAndReviewRows()
    {
        var source = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(
            @"src\Meridian.Wpf\Views\AccountingClosePage.xaml"));

        source.Should().Contain("AutomationProperties.AutomationId=\"AccountingClosePage\"");
        source.Should().Contain("Command=\"{Binding LoadClosePlanCommand}\"");
        source.Should().Contain("Command=\"{Binding ConfigureClosePlanCommand}\"");
        source.Should().Contain("Command=\"{Binding SignOffCloseTaskCommand}\"");
        source.Should().Contain("Command=\"{Binding ReviewLateAdjustmentCommand}\"");
        source.Should().Contain("Command=\"{Binding LockClosePeriodCommand}\"");
        source.Should().Contain("ItemsSource=\"{Binding CloseMaterialityRows}\"");
        source.Should().Contain("ItemsSource=\"{Binding CloseTaskRows}\"");
        source.Should().Contain("ItemsSource=\"{Binding CloseDependencyRows}\"");
        source.Should().Contain("ItemsSource=\"{Binding CloseSignOffMatrixRows}\"");
        source.Should().Contain("ItemsSource=\"{Binding CloseLateAdjustmentRows}\"");
        source.Should().Contain("ItemsSource=\"{Binding CloseEvidenceReviewRows}\"");
        source.Should().Contain("ItemsSource=\"{Binding ClosePeriodLockIssueRows}\"");
    }

    private static object? GetInjectedDependency(
        AccountingPostingCandidatePostService service,
        string fieldName)
        => typeof(AccountingPostingCandidatePostService)
            .GetField(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.GetValue(service);
}
