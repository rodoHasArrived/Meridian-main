using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.FinancialOperations.Ledger;
using Meridian.Instruments.FixedIncome;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.Ledger;
using Meridian.Storage.SecurityMaster;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed class BondIncomeAuthorityCompositionTests
{
    [Theory]
    [InlineData("security")]
    [InlineData("ledger")]
    [InlineData("policy")]
    public void PartialHost_LeavesIncomeAuthorityUnavailable_AndPerformanceResolvable(string missing)
    {
        using var provider = BuildBondServices(missing);

        provider.GetService<IBondIncomeService>().Should().BeNull();
        provider.GetService<BondIncomePreparationService>().Should().BeNull();
        provider.GetService<IBondCouponRecognitionService>().Should().BeNull();
        provider.GetRequiredService<IBondPerformanceService>().Should().BeOfType<BondPerformanceService>();
    }

    [Fact]
    public void ConfiguredHost_ResolvesNativeIncomePreparationAndCouponAuthorities()
    {
        using var provider = BuildBondServices(missing: null);

        provider.GetRequiredService<IBondIncomeService>().Should().BeOfType<BondIncomeService>();
        provider.GetRequiredService<BondIncomePreparationService>().Should().NotBeNull();
        provider.GetRequiredService<IBondCouponRecognitionService>().Should().BeOfType<BondCouponRecognitionService>();
        provider.GetRequiredService<IBondPerformanceService>().Should().BeOfType<BondPerformanceService>();
    }

    private static ServiceProvider BuildBondServices(string? missing)
    {
        IServiceCollection services = new ServiceCollection();
        if (missing != "security")
            services.AddSingleton(Substitute.For<ISecurityMasterStore>());
        if (missing != "ledger")
            services.AddSingleton(Substitute.For<ILedgerJournalStore>());
        if (missing != "policy")
            services.AddSingleton(Substitute.For<IAccountingPolicyService>());
        services.AddSingleton(Substitute.For<IInstrumentPositionProjectionStore>());
        services.AddSingleton(Substitute.For<IBondCouponRecognitionStore>());
        services.AddSingleton(Substitute.For<IAccountingConfigurationService>());
        services.AddSingleton(Substitute.For<IAssetAccountingEventSpineService>());
        services.AddSingleton(Substitute.For<IBondIncomePreparationEvidenceStore>());
        services.AddSingleton(Substitute.For<IAccountBondHoldingsSource>());
        services.AddSingleton(Substitute.For<IBondWorkflowService>());
        services.AddSingleton(Substitute.For<IBondMonitoringService>());
        services.AddSingleton(Substitute.For<ISecurityMasterQueryService>());

        // Exercise the production registrations without unrelated workstation services. The
        // complete dependency set remains registered before workstation composition, as in UiServer.
        IServiceCollection workstation = new ServiceCollection();
        foreach (var dependency in services)
            workstation.Add(dependency);
        workstation.AddWorkstationSharedServices();
        var bondServices = new[]
        {
            typeof(IBondIncomeService), typeof(BondIncomePreparationService),
            typeof(IBondCouponRecognitionService), typeof(IBondPerformanceService)
        };
        foreach (var descriptor in workstation.Where(descriptor => bondServices.Contains(descriptor.ServiceType)))
            services.Add(descriptor);

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }
}
