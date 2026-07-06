using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Meridian.Core.Config;
using Meridian.Wpf.Shell.Services;

namespace Meridian.Wpf.Features;

public static class DesktopFeatureModuleRegistry
{
    private static readonly Func<IDesktopFeatureModule>[] ModuleFactories =
    [
        DesktopFeature.Module<Home.HomeFeatureModule>(),
        DesktopFeature.Module<Trading.TradingFeatureModule>(),
        DesktopFeature.Module<Portfolio.PortfolioFeatureModule>(),
        DesktopFeature.Module<Accounting.AccountingFeatureModule>(),
        DesktopFeature.Module<Reporting.ReportingFeatureModule>(),
        DesktopFeature.Module<Strategy.StrategyFeatureModule>(),
        DesktopFeature.Module<Data.DataFeatureModule>(),
        DesktopFeature.Module<Settings.SettingsFeatureModule>()
    ];

    public static IServiceCollection AddMeridianWpfFeatureModules(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(ShellPageRegistryBuilder.BuildDefault());

        foreach (var module in GetModules())
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

    public static IReadOnlyList<IDesktopFeatureModule> GetModules()
        => ModuleFactories.Select(static createModule => createModule()).ToArray();

    public static IReadOnlyList<FeatureCapabilityDescriptor> GetCapabilities()
        => GetModules()
            .SelectMany(static module => module.DeclareCapabilities())
            .GroupBy(static capability => capability.CapabilityKey, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static capability => capability.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
}

public static class DesktopFeature
{
    public static Func<IDesktopFeatureModule> Module<T>()
        where T : IDesktopFeatureModule, new()
        => static () => new T();
}
