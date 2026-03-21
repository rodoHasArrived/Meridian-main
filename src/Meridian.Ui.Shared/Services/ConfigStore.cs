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

    public static Meridian.Application.Config.AppConfig LoadConfig(string path)
        => CoreConfigStore.LoadConfig(path);

    public Meridian.Application.Config.AppConfig Load() => _core.Load();

    public System.Threading.Tasks.Task SaveAsync(Meridian.Application.Config.AppConfig cfg)
        => _core.SaveAsync(cfg);

    public Meridian.Application.Monitoring.ProviderMetricsStatus? TryLoadProviderMetrics()
        => _core.TryLoadProviderMetrics();

    public string? TryLoadStatusJson() => _core.TryLoadStatusJson();

    public string GetDataRoot(Meridian.Application.Config.AppConfig? cfg = null)
        => _core.GetDataRoot(cfg);

    public string GetStatusPath(Meridian.Application.Config.AppConfig? cfg = null)
        => _core.GetStatusPath(cfg);

    public string GetBackfillStatusPath(Meridian.Application.Config.AppConfig? cfg = null)
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
