using Meridian.Core.Config;
using Meridian.Infrastructure.Adapters.Core;

namespace Meridian.Ui.Shared.Services;

internal static class AlpacaFeedConfiguration
{
    internal static string SavedFeed(AppConfig config) => ModuleFeed(config) ?? config.Alpaca?.Feed ?? "iex";

    internal static string? ModuleFeed(AppConfig config)
    {
        var settings = config.ProviderModules?.Modules?.LastOrDefault(IsAlpacaModule).Value?.Settings;
        var feed = settings?.LastOrDefault(IsFeedSetting).Value;
        return string.IsNullOrWhiteSpace(feed) ? null : feed;
    }

    internal static AppConfig WithFeed(AppConfig config, string feed, string? sourceId)
    {
        var updated = config;
        if (sourceId is not null)
        {
            var section = config.DataSources;
            var sources = section?.Sources ?? [];
            var selected = sources.Where(source => source.Id.Equals(sourceId, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (selected is not [{ Provider: DataSourceKind.Alpaca } source])
                throw new ArgumentException("Select an existing Alpaca data source.");
            if (!feed.Equals(source.Alpaca?.Feed, StringComparison.Ordinal))
            {
                var changed = source with { Alpaca = (source.Alpaca ?? new AlpacaOptions()) with { Feed = feed } };
                updated = updated with { DataSources = section! with { Sources = sources.Select(candidate => candidate == source ? changed : candidate).ToArray() } };
            }
        }

        // The runtime constructs the Alpaca provider from host options. Keep an explicit
        // module override in sync so it cannot shadow a verified host-wide selection.
        if (!feed.Equals(updated.Alpaca?.Feed ?? "iex", StringComparison.Ordinal))
            updated = updated with { Alpaca = (updated.Alpaca ?? new AlpacaOptions()) with { Feed = feed } };
        if (updated.ProviderModules?.Modules is { } modules &&
            modules.LastOrDefault(IsAlpacaModule) is { Key: not null, Value.Settings: { } settings } module &&
            settings.LastOrDefault(IsFeedSetting).Key is { } feedKey &&
            !feed.Equals(settings[feedKey], StringComparison.Ordinal))
        {
            var nextSettings = new Dictionary<string, string?>(settings, settings.Comparer) { [feedKey] = feed };
            var nextModules = new Dictionary<string, ProviderModuleSettings>(modules, modules.Comparer)
            {
                [module.Key] = module.Value with { Settings = nextSettings }
            };
            updated = updated with { ProviderModules = updated.ProviderModules with { Modules = nextModules } };
        }
        return updated;
    }

    private static bool IsAlpacaModule(KeyValuePair<string, ProviderModuleSettings> module)
        => ProviderIdentity.EqualsId(module.Key, "alpaca");

    private static bool IsFeedSetting(KeyValuePair<string, string?> setting)
        => setting.Key.Equals("feed", StringComparison.OrdinalIgnoreCase);
}
