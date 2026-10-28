using Meridian.Contracts.Domain.Enums;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Infrastructure.Adapters.Fred;

/// <summary>
/// Owns FRED's historical and series-search factories. Configuration is evaluated against the
/// host's final credential resolver without connecting to FRED during registration.
/// </summary>
public sealed class FredProviderModule : ConfigurableProviderModuleBase,
    IProviderModuleCapabilityAvailability, IProviderModuleCredentialHints
{
    public override string ModuleId => "fred";
    public override string ModuleDisplayName => "FRED Economic Data";
    public IReadOnlyList<string> CredentialKeyHints => ["apiKey"];
    public override ProviderCapabilities[] Capabilities =>
        [new() { SupportsBackfill = true }, ProviderCapabilities.SymbolSearch];

    public static ProviderCapabilityDescriptor Descriptor { get; } = new(
        "fred", Historical: typeof(FredHistoricalDataProvider), Search: typeof(FredSymbolSearchProvider),
        InstrumentTypes: [InstrumentType.Index],
        HistoricalFactory: static factory => CreateHistorical(factory),
        SearchFactory: static factory => CreateSearch(factory),
        FactoryOwner: ProviderCapabilityFactoryOwner.Module);

    public override void Register(IServiceCollection services, DataSourceRegistry registry)
    {
        services.AddSingleton<FredHistoricalDataProvider>(provider =>
            CreateHistorical(provider.GetRequiredService<ProviderFactory>(), GetApiKey())
            ?? throw new InvalidOperationException("FRED has no configured historical capability."));
        services.AddSingleton<FredSymbolSearchProvider>(provider =>
            CreateSearch(provider.GetRequiredService<ProviderFactory>(), GetApiKey())
            ?? throw new InvalidOperationException("FRED has no configured symbol-search capability."));
    }

    public bool IsCapabilityConfigured(Type contract, IServiceProvider services)
    {
        var factory = services.GetRequiredService<ProviderFactory>();
        try
        {
            return ResolveApiKey(factory, contract, GetApiKey()) is not null;
        }
        catch (Exception ex)
        {
            // Match the standalone factory's failure isolation without replacing failed
            // credentials with another source or fabricating an available adapter.
            factory.Log.Warning(ex, "Failed to configure {Provider} capability {Capability}; skipping it for this run",
                ModuleId, contract.Name);
            return false;
        }
    }

    private static FredHistoricalDataProvider? CreateHistorical(ProviderFactory factory, string? moduleApiKey = null)
        => ResolveApiKey(factory, typeof(IHistoricalDataProvider), moduleApiKey) is { } key
            ? new FredHistoricalDataProvider(apiKey: key, log: factory.Log)
            : null;

    private static FredSymbolSearchProvider? CreateSearch(ProviderFactory factory, string? moduleApiKey = null)
        => ResolveApiKey(factory, typeof(ISymbolSearchProvider), moduleApiKey) is { } key
            ? new FredSymbolSearchProvider(apiKey: key, log: factory.Log)
            : null;

    private static string? ResolveApiKey(ProviderFactory factory, Type contract, string? moduleApiKey)
    {
        var config = factory.Config.Backfill?.Providers?.Fred;
        // Retain the historical lane's opt-in rule and the search lane's credential-based default.
        var enabled = contract == typeof(IHistoricalDataProvider) ? config?.Enabled == true
            : contract == typeof(ISymbolSearchProvider) && config?.Enabled != false;
        if (!factory.IsFamilyEnabled("fred") || !enabled)
            return null;

        var key = moduleApiKey ?? factory.CreateCredentialContext<FredHistoricalDataProvider>(
            ("FRED_API_KEY", config?.ApiKey))?.Get("FRED_API_KEY");
        return string.IsNullOrWhiteSpace(key) ? null : key;
    }
}
