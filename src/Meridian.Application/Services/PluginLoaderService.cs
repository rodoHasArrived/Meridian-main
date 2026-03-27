using System.Reflection;
using Microsoft.Extensions.Logging;
using Meridian.Infrastructure.DataSources;

namespace Meridian.Application.Services;

/// <summary>
/// Result of attempting to load a plugin assembly.
/// </summary>
public sealed class PluginLoadResult
{
    /// <summary>Full path to the assembly that was loaded (or attempted to load).</summary>
    public string AssemblyPath { get; init; } = "";

    /// <summary>Whether the plugin loaded and registered successfully.</summary>
    public bool Success { get; init; }

    /// <summary>If <see cref="Success"/> is false, contains the error message.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Names of types (fully qualified) registered as data sources from this plugin.</summary>
    public IReadOnlyList<string> RegisteredTypes { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Service for loading and registering data source plugins from external assemblies.
/// Supports dynamic plugin discovery via reflection of [DataSource] attributes (ADR-005).
/// </summary>
public interface IPluginLoaderService
{
    /// <summary>
    /// Loads all <c>*.dll</c> files (non-recursive) in <paramref name="pluginsDirectory"/>,
    /// discovers <c>[DataSource]</c>-decorated types, and registers them with the
    /// <see cref="DataSourceRegistry"/>. Assemblies that fail to load are skipped with a warning.
    /// </summary>
    Task<IReadOnlyList<PluginLoadResult>> LoadPluginsAsync(string pluginsDirectory, CancellationToken ct);

    /// <summary>Gets the results from the most recent <see cref="LoadPluginsAsync"/> call.</summary>
    IReadOnlyList<PluginLoadResult> LoadedPlugins { get; }
}

/// <summary>
/// Default implementation of <see cref="IPluginLoaderService"/>.
/// </summary>
[ImplementsAdr("ADR-005", "Plugin-based provider discovery")]
public sealed class PluginLoaderService : IPluginLoaderService
{
    private readonly DataSourceRegistry _registry;
    private readonly ILogger<PluginLoaderService> _logger;
    private IReadOnlyList<PluginLoadResult> _loadedPlugins = [];

    public IReadOnlyList<PluginLoadResult> LoadedPlugins => _loadedPlugins;

    public PluginLoaderService(DataSourceRegistry registry, ILogger<PluginLoaderService> logger)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(logger);
        _registry = registry;
        _logger = logger;
    }

    public async Task<IReadOnlyList<PluginLoadResult>> LoadPluginsAsync(string pluginsDirectory, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginsDirectory);

        _logger.LogInformation("Loading plugins from directory: {PluginDirectory}", pluginsDirectory);

        if (!Directory.Exists(pluginsDirectory))
        {
            _logger.LogWarning("Plugin directory does not exist: {PluginDirectory}", pluginsDirectory);
            _loadedPlugins = [];
            return _loadedPlugins;
        }

        var dllFiles = Directory.EnumerateFiles(pluginsDirectory, "*.dll", SearchOption.TopDirectoryOnly);
        var results = new List<PluginLoadResult>();

        foreach (var dllPath in dllFiles)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Yield(); // yield between files to remain cooperative

            var result = LoadSinglePlugin(dllPath);
            results.Add(result);

            if (result.Success)
                _logger.LogInformation("Loaded plugin {AssemblyPath} — {TypeCount} type(s) registered",
                    dllPath, result.RegisteredTypes.Count);
            else
                _logger.LogWarning("Plugin {AssemblyPath} failed to load: {ErrorMessage}",
                    dllPath, result.ErrorMessage);
        }

        _logger.LogInformation("Plugin scan complete: {SuccessCount}/{Total} succeeded",
            results.Count(r => r.Success), results.Count);

        _loadedPlugins = results.AsReadOnly();
        return _loadedPlugins;
    }

    private PluginLoadResult LoadSinglePlugin(string assemblyPath)
    {
        Assembly assembly;
        try
        {
            assembly = Assembly.LoadFrom(assemblyPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Assembly.LoadFrom failed for {AssemblyPath}", assemblyPath);
            return new PluginLoadResult
            {
                AssemblyPath = assemblyPath,
                Success = false,
                ErrorMessage = $"{ex.GetType().Name}: {ex.Message}"
            };
        }

        // Collect [DataSource]-decorated types; handle broken assemblies gracefully.
        var dataSourceTypes = GetDataSourceTypes(assembly, assemblyPath);

        if (dataSourceTypes.Count == 0)
        {
            return new PluginLoadResult { AssemblyPath = assemblyPath, Success = true };
        }

        // Register the assembly once — DataSourceRegistry skips duplicates by ID.
        try
        {
            _registry.DiscoverFromAssemblies(assembly);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DataSourceRegistry.DiscoverFromAssemblies failed for {AssemblyPath}", assemblyPath);
            return new PluginLoadResult
            {
                AssemblyPath = assemblyPath,
                Success = false,
                ErrorMessage = $"Registry error: {ex.Message}"
            };
        }

        var names = dataSourceTypes.Select(t => t.FullName ?? t.Name).ToList().AsReadOnly();
        _logger.LogDebug("Registered types from {AssemblyPath}: {Types}", assemblyPath, names);

        return new PluginLoadResult
        {
            AssemblyPath = assemblyPath,
            Success = true,
            RegisteredTypes = names
        };
    }

    private List<Type> GetDataSourceTypes(Assembly assembly, string assemblyPath)
    {
        IEnumerable<Type> types;
        try
        {
            types = assembly.GetExportedTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            _logger.LogWarning(
                "ReflectionTypeLoadException scanning {AssemblyPath} — {Count} loader exception(s). Proceeding with partial type list.",
                assemblyPath, ex.LoaderExceptions?.Length ?? 0);

            foreach (var loaderEx in ex.LoaderExceptions ?? [])
                _logger.LogDebug("  Loader exception: {Message}", loaderEx?.Message);

            types = ex.Types.Where(t => t is not null)!;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not enumerate exported types in {AssemblyPath}", assemblyPath);
            return [];
        }

        return types.Where(t => t.IsDataSource()).ToList();
    }
}
