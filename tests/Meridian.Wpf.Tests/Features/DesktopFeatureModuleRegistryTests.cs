using Meridian.Wpf.Features;

namespace Meridian.Wpf.Tests.Features;

public sealed class DesktopFeatureModuleRegistryTests
{
    [Fact]
    public void GetModules_ShouldReturnAllExpectedModulesInStableStartupOrder()
    {
        DesktopFeatureModuleRegistry.GetModules()
            .Select(static module => module.GetType())
            .Should()
            .Equal(
                typeof(Meridian.Wpf.Features.Home.HomeFeatureModule),
                typeof(Meridian.Wpf.Features.Trading.TradingFeatureModule),
                typeof(Meridian.Wpf.Features.Portfolio.PortfolioFeatureModule),
                typeof(Meridian.Wpf.Features.Accounting.AccountingFeatureModule),
                typeof(Meridian.Wpf.Features.Reporting.ReportingFeatureModule),
                typeof(Meridian.Wpf.Features.Strategy.StrategyFeatureModule),
                typeof(Meridian.Wpf.Features.Data.DataFeatureModule),
                typeof(Meridian.Wpf.Features.Settings.SettingsFeatureModule));
    }

    [Fact]
    public void GetModules_ShouldReturnFreshModulesFromDeclarativeFactories()
    {
        var first = DesktopFeatureModuleRegistry.GetModules();
        var second = DesktopFeatureModuleRegistry.GetModules();

        first.Select(static module => module.GetType()).Should().Equal(second.Select(static module => module.GetType()));
        first.Zip(second, static (left, right) => ReferenceEquals(left, right))
            .Should()
            .OnlyContain(static sameInstance => sameInstance is false);
    }

    [Fact]
    public void GetCapabilities_ShouldKeepCapabilityKeysUniqueAfterAggregation()
    {
        var duplicateKeys = DesktopFeatureModuleRegistry.GetCapabilities()
            .GroupBy(static capability => capability.CapabilityKey, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToArray();

        duplicateKeys.Should().BeEmpty();
    }
}
