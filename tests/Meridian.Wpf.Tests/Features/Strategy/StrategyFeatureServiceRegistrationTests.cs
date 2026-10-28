using Meridian.Wpf.Tests.Features;
using Meridian.Application.SecurityMaster;
using Meridian.Backtesting;
using Meridian.Contracts.SecurityMaster;
using Meridian.Infrastructure.Adapters.Polygon;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Services;
using Meridian.Wpf.Features.Strategy;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Meridian.Wpf.Tests.Features.Strategy;

public sealed class StrategyFeatureServiceRegistrationTests
{
    [Fact]
    public async Task Register_WithBothSecurityMasterContractFamilies_ResolvesOneCanonicalAdjustmentService()
    {
        using var env = new EnvironmentVariableScope()
            .Set("MERIDIAN_SECURITY_MASTER_CONNECTION_STRING", "Host=localhost;Database=unused");
        var services = new ServiceCollection();
        new StrategyFeatureModule().Register(services);
        var securityId = Guid.NewGuid();
        var aggregateQuery = new Mock<ISecurityMasterQueryService>(MockBehavior.Strict);
        var corporateActions = new Mock<ISecurityCorporateActionsQueryService>(MockBehavior.Strict);
        corporateActions.Setup(query => query.GetCorporateActionsAsync(securityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<CorporateActionDto>)Array.Empty<CorporateActionDto>());
        var resolver = new Mock<Meridian.Contracts.SecurityMaster.ISecurityResolver>(MockBehavior.Strict);
        resolver.Setup(value => value.ResolveAsync(It.IsAny<ResolveSecurityRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(securityId);
        services.AddSingleton(aggregateQuery.Object);
        services.AddSingleton(corporateActions.Object);
        services.AddSingleton(resolver.Object);
        services.AddSingleton<ILogger<CorporateActionAdjustmentService>>(NullLogger<CorporateActionAdjustmentService>.Instance);
        using var provider = services.BuildServiceProvider();

        // Both public four-argument constructors remain usable for compiled callers, and
        // both families are resolvable after the module adds its compatibility aliases.
        var constructors = typeof(CorporateActionAdjustmentService).GetConstructors();
        constructors.Should().HaveCount(2);
        foreach (var parameter in constructors.SelectMany(constructor => constructor.GetParameters()))
            provider.GetService(parameter.ParameterType).Should().NotBeNull();

        var adjustment = provider.GetRequiredService<CorporateActionAdjustmentService>();
        provider.GetRequiredService<ICorporateActionAdjustmentService>().Should().BeSameAs(adjustment);
        provider.GetRequiredService<ILivePositionCorporateActionAdjuster>().Should().BeSameAs(adjustment);
        provider.GetRequiredService<CorporateActionAdjustmentService>().Should().BeSameAs(adjustment);
        await adjustment.PrepareAsync([], "SPY", new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero));

        corporateActions.Verify(query => query.GetCorporateActionsAsync(securityId, It.IsAny<CancellationToken>()), Times.Once);
        resolver.Verify(value => value.ResolveAsync(It.IsAny<ResolveSecurityRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        aggregateQuery.VerifyNoOtherCalls();
    }

    [Fact]
    public void Register_WithoutSecurityMasterConnection_ShouldOwnStrategyServicesWithConfiguredLifetimes()
    {
        using var env = new EnvironmentVariableScope().Set("MERIDIAN_SECURITY_MASTER_CONNECTION_STRING", null);
        var services = new ServiceCollection();

        new StrategyFeatureModule().Register(services);

        services.SingleDescriptor<ISecurityMasterService>().Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.SingleDescriptor<ISecurityMasterImportService>().Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.SingleDescriptor<ITradingParametersBackfillService>().Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.SingleDescriptor<IStrategyRepository>().Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.SingleDescriptor<StrategyRunReadService>().Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.SingleDescriptor<StrategyRunContinuityService>().Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.SingleDescriptor<WorkstationWorkflowSummaryService>().Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.SingleDescriptor<IWorkstationStrategyBriefingApiClient>().Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.SingleDescriptor<IWorkstationOperatorInboxApiClient>().Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.SingleDescriptor<IStrategyBriefingWorkspaceService>().Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.SingleDescriptor<StrategyRunWorkspaceService>().Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.SingleDescriptor<BacktestDataAvailabilityService>().Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.SingleDescriptor<RunMatViewModel>().Lifetime.Should().Be(ServiceLifetime.Transient);
        services.SingleDescriptor<StrategyRunBrowserViewModel>().Lifetime.Should().Be(ServiceLifetime.Transient);
        services.SingleDescriptor<StrategyRunDetailViewModel>().Lifetime.Should().Be(ServiceLifetime.Transient);
        services.SingleDescriptor<StrategyRunPortfolioViewModel>().Lifetime.Should().Be(ServiceLifetime.Transient);
        services.SingleDescriptor<StrategyRunLedgerViewModel>().Lifetime.Should().Be(ServiceLifetime.Transient);
        services.SingleDescriptor<CashFlowViewModel>().Lifetime.Should().Be(ServiceLifetime.Transient);
        services.SingleDescriptor<BacktestViewModel>().Lifetime.Should().Be(ServiceLifetime.Transient);
        services.SingleDescriptor<BatchBacktestViewModel>().Lifetime.Should().Be(ServiceLifetime.Transient);
        services.SingleDescriptor<ChartingPageViewModel>().Lifetime.Should().Be(ServiceLifetime.Transient);
        services.SingleDescriptor<TickerStripViewModel>().Lifetime.Should().Be(ServiceLifetime.Transient);
        services.SingleDescriptor<QuantScriptViewModel>().Lifetime.Should().Be(ServiceLifetime.Transient);
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly Dictionary<string, string?> _originalValues = new(StringComparer.Ordinal);

        public EnvironmentVariableScope Set(string name, string? value)
        {
            if (!_originalValues.ContainsKey(name))
            {
                _originalValues[name] = Environment.GetEnvironmentVariable(name);
            }

            Environment.SetEnvironmentVariable(name, value);
            return this;
        }

        public void Dispose()
        {
            foreach (var entry in _originalValues)
            {
                Environment.SetEnvironmentVariable(entry.Key, entry.Value);
            }
        }
    }
}
