// Thin wrapper around the core ConfigStore that provides the web dashboard default path.
// New code should reference Meridian.Application.UI.ConfigStore directly when possible.
using CoreConfigStore = Meridian.Application.UI.ConfigStore;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// ConfigStore for web dashboard hosting.
/// Delegates all operations to the core <see cref="CoreConfigStore"/> and only provides
/// a web-specific default config path (4 directories up from BaseDirectory).
/// </summary>
public sealed class ConfigStore
{
    private readonly CoreConfigStore _core;

    public ConfigStore() : this(GetWebDefaultPath()) { }

    public ConfigStore(string? configPath)
    {
        _core = new CoreConfigStore(configPath);
    }

    public string ConfigPath => _core.ConfigPath;

    public static Meridian.Core.Config.AppConfig LoadConfig(string path)
        => CoreConfigStore.LoadConfig(path);

    public Meridian.Core.Config.AppConfig Load() => _core.Load();

    public Meridian.Contracts.Configuration.ConfigurationChangeStatus GetChangeStatus() => _core.GetChangeStatus();

    public System.Threading.Tasks.Task SaveAsync(Meridian.Core.Config.AppConfig cfg)
        => SaveAsync(cfg, CancellationToken.None);

    public System.Threading.Tasks.Task SaveAsync(
        Meridian.Core.Config.AppConfig cfg,
        CancellationToken ct)
        => _core.UpdateAsync(current =>
        {
            UiConfigurationSaveGuard.Validate(current, cfg, confirmDataSemantics: false, wholeDocument: true);
            return cfg;
        }, ct);

    /// <summary>Read-modify-write under the configuration lock; see the core store for the contract.</summary>
    public System.Threading.Tasks.Task<T> UpdateAsync<T>(
        Func<Meridian.Core.Config.AppConfig, (Meridian.Core.Config.AppConfig Config, T Result)> update,
        CancellationToken ct = default)
        => UpdateWithConfirmationAsync(update, confirmDataSemantics: false, ct);

    /// <summary>Explicit confirmation is required for edits changing the meaning of newly stored data.</summary>
    public System.Threading.Tasks.Task<T> UpdateWithConfirmationAsync<T>(
        Func<Meridian.Core.Config.AppConfig, (Meridian.Core.Config.AppConfig Config, T Result)> update,
        bool confirmDataSemantics,
        CancellationToken ct = default)
        => _core.UpdateAsync(current =>
        {
            var (next, result) = update(current);
            UiConfigurationSaveGuard.Validate(current, next, confirmDataSemantics, wholeDocument: false);
            return (next, result);
        }, ct);

    /// <summary>Read-modify-write under the configuration lock; see the core store for the contract.</summary>
    public System.Threading.Tasks.Task UpdateAsync(
        Func<Meridian.Core.Config.AppConfig, Meridian.Core.Config.AppConfig> update,
        CancellationToken ct = default)
        => UpdateWithConfirmationAsync<object?>(current => (update(current), null), confirmDataSemantics: false, ct);

    public Meridian.DataIntegration.Monitoring.ProviderMetricsStatus? TryLoadProviderMetrics()
        => _core.TryLoadProviderMetrics();

    public string? TryLoadStatusJson() => _core.TryLoadStatusJson();

    public string GetDataRoot(Meridian.Core.Config.AppConfig? cfg = null)
        => _core.GetDataRoot(cfg);

    public string GetStatusPath(Meridian.Core.Config.AppConfig? cfg = null)
        => _core.GetStatusPath(cfg);

    public string GetBackfillStatusPath(Meridian.Core.Config.AppConfig? cfg = null)
        => _core.GetBackfillStatusPath(cfg);

    private static string GetWebDefaultPath()
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "appsettings.json"));
}

/// <summary>
/// Configures the core ConfigStore default path resolver for web dashboard hosting.
/// </summary>
public static class ConfigStoreExtensions
{
    public static void UseWebDefaultPath()
    {
        CoreConfigStore.DefaultPathResolver = () =>
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "appsettings.json"));
    }
}
