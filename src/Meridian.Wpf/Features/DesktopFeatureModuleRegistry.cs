using Meridian.Core.Config;
using Meridian.Wpf.Shell.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Wpf.Features;

public static class DesktopFeatureModuleRegistry
{
    private static readonly IDesktopFeatureModule[] Modules =
    [
        new Home.HomeFeatureModule(),
        new Trading.TradingFeatureModule(),
        new Portfolio.PortfolioFeatureModule(),
        new Accounting.AccountingFeatureModule(),
        new Reporting.ReportingFeatureModule(),
        new Strategy.StrategyFeatureModule(),
        new Data.DataFeatureModule(),
        new Settings.SettingsFeatureModule()
    ];

    public static IServiceCollection AddMeridianWpfFeatureModules(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(ShellPageRegistryBuilder.BuildDefault());

        foreach (var module in Modules)
        {
            module.Register(services);
        }

        return services;
    }

    public static IServiceCollection AddMeridianWpfFeatureModules(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<FeatureCapabilityOptions>(configuration.GetSection("FeatureCapabilities"));
        services.AddSingleton<IReadOnlyList<FeatureCapabilityDescriptor>>(_ => GetCapabilities());
        services.AddSingleton<IEnumerable<FeatureCapabilityDescriptor>>(sp => sp.GetRequiredService<IReadOnlyList<FeatureCapabilityDescriptor>>());
        services.AddSingleton<IFeatureCapabilityGate, FeatureCapabilityGateService>();

        return services.AddMeridianWpfFeatureModules();
    }

    public static IReadOnlyList<IDesktopFeatureModule> GetModules() => Modules;

    public static IReadOnlyList<FeatureCapabilityDescriptor> GetCapabilities()
        => Modules
            .SelectMany(static module => module.DeclareCapabilities())
            .GroupBy(static capability => capability.CapabilityKey, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static capability => capability.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
