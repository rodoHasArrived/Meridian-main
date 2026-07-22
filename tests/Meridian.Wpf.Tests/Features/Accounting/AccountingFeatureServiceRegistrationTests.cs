using Meridian.Wpf.Tests.Features;
using Meridian.Application.FundStructure;
using Meridian.Contracts.Services;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.AccountingClose;
using Meridian.FinancialOperations.PrivateCapital;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Ui.Services.Services.Accounting;
using Meridian.Ui.Shared.Services;
using Meridian.Wpf.Features.Accounting;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Wpf.Tests.Features.Accounting;

public sealed class AccountingFeatureServiceRegistrationTests
{
    [Fact]
    public void Register_ShouldOwnAccountingFeatureServicesWithConfiguredLifetimes()
    {
        var services = new ServiceCollection();

        new AccountingFeatureModule().Register(services);

        services.SingleDescriptor<InMemoryFundAccountService>().Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.SingleDescriptor<IFundAccountService>().Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.SingleDescriptor<IFundStructureService>().Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.SingleDescriptor<FundStructureSetupWorkflowService>().Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.SingleDescriptor<FundAccountReadService>().Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.SingleDescriptor<FundLedgerReadService>().Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.SingleDescriptor<ReconciliationReadService>().Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.SingleDescriptor<CashFinancingReadService>().Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.SingleDescriptor<IWorkstationReconciliationApiClient>().Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.SingleDescriptor<IWorkstationSecurityMasterApiClient>().Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.SingleDescriptor<ISecurityAssetProfileWorkflowClient>().Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.SingleDescriptor<IOperationsControlCenterClient>().Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.SingleDescriptor<IFundReconciliationWorkbenchService>().Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.SingleDescriptor<IStatementReconciliationWorkbenchService>().Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.SingleDescriptor<IAccountingCloseManagementService>().Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.SingleDescriptor<IPrivateCapitalCloseCockpitService>().Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.SingleDescriptor<FundStructureSetupViewModel>().Lifetime.Should().Be(ServiceLifetime.Transient);
        services.SingleDescriptor<FundAccountsViewModel>().Lifetime.Should().Be(ServiceLifetime.Transient);
        services.SingleDescriptor<FundLedgerViewModel>().Lifetime.Should().Be(ServiceLifetime.Transient);
        services.SingleDescriptor<FinancialRecordExplorerViewModel>().Lifetime.Should().Be(ServiceLifetime.Transient);
        services.SingleDescriptor<AccountPortfolioViewModel>().Lifetime.Should().Be(ServiceLifetime.Transient);
    }

    [Fact]
    public void Register_ShouldResolveAccountingEvidenceServicesThroughInterfaces()
    {
        var services = new ServiceCollection();

        new AccountingFeatureModule().Register(services);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IAutomatedJournalCapitalAccountReconciliationResolver>()
            .Should()
            .BeOfType<LedgerCapitalAccountReconciliationResolver>();
        provider.GetRequiredService<IAccountingPositionSnapshotCaptureService>()
            .Should()
            .BeOfType<AccountingPositionSnapshotCaptureService>();
    }
}
