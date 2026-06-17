using Meridian.Wpf.Tests.Features;
using Meridian.Backtesting;
using Meridian.Contracts.SecurityMaster;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Services;
using Meridian.Wpf.Features.Strategy;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Wpf.Tests.Features.Strategy;

public sealed class StrategyFeatureServiceRegistrationTests
{
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
