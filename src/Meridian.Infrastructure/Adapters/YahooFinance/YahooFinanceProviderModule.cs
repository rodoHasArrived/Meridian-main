using Meridian.Contracts.Domain.Enums;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Infrastructure.Adapters.YahooFinance;

/// <summary>
/// Owns Yahoo Finance's historical factory and capability declaration. Registration does not
/// contact Yahoo; availability is evaluated against the final host configuration.
/// </summary>
public sealed class YahooFinanceProviderModule : ConfigurableProviderModuleBase,
    IProviderModuleCapabilityAvailability
{
    public override string ModuleId => "yahoo";
    public override string ModuleDisplayName => "Yahoo Finance (free)";
    public override ProviderCapabilities[] Capabilities => [new() { SupportsBackfill = true }];

    public static ProviderCapabilityDescriptor Descriptor { get; } = new(
        "yahoo", Historical: typeof(YahooFinanceHistoricalDataProvider),
        InstrumentTypes: [InstrumentType.Equity, InstrumentType.Index, InstrumentType.Forex, InstrumentType.Crypto],
        HistoricalFactory: static factory => CreateHistorical(factory),
        FactoryOwner: ProviderCapabilityFactoryOwner.Module);

    public override void Register(IServiceCollection services, DataSourceRegistry registry)
    {
        services.AddSingleton<YahooFinanceHistoricalDataProvider>(provider =>
            CreateHistorical(provider.GetRequiredService<ProviderFactory>())
            ?? throw new InvalidOperationException("Yahoo Finance has no configured historical capability."));
    }

    public bool IsCapabilityConfigured(Type contract, IServiceProvider services)
        => contract == typeof(IHistoricalDataProvider)
            && IsEnabled(services.GetRequiredService<ProviderFactory>());

    private static bool IsEnabled(ProviderFactory factory)
        => factory.IsFamilyEnabled("yahoo") && factory.Config.Backfill?.Providers?.Yahoo?.Enabled != false;

    private static YahooFinanceHistoricalDataProvider? CreateHistorical(ProviderFactory factory)
        => IsEnabled(factory) ? new YahooFinanceHistoricalDataProvider(log: factory.Log,
            runtimeOptions: BackfillProviderRuntimeOptions.FromConfig(factory.Config.Backfill?.Providers, "yahoo")) : null;
}
