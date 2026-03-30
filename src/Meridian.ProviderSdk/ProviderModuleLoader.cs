using System.Reflection;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>
/// Discovers, validates, and loads <see cref="IProviderModule"/> implementations from one or
/// more assemblies into a DI service collection.
/// </summary>
/// <remarks>
/// <see cref="ProviderModuleLoader"/> separates module lifecycle management from
/// <see cref="DataSourceRegistry"/>, which handles attribute-based discovery.
/// The two systems are complementary:
/// <list type="bullet">
///   <item><description>Attribute discovery (<c>[DataSource]</c>) — single-class registration</description></item>
///   <item><description>Module loading — multi-capability, grouped registration per provider</description></item>
/// </list>
/// Typical usage:
/// <code>
/// var loader = new ProviderModuleLoader();
/// var report = await loader.LoadFromAssembliesAsync(services, registry, ct,
///     typeof(AlpacaProviderModule).Assembly);
/// </code>
/// </remarks>
[ImplementsAdr("ADR-001", "Centralized module-based provider loading")]
[ImplementsAdr("ADR-005", "Assembly-scanning provider discovery for modules")]
public sealed class ProviderModuleLoader
{
    private readonly ILogger<ProviderModuleLoader> _log;

    /// <param name="log">Optional logger; falls back to the null logger when omitted.</param>
    public ProviderModuleLoader(ILogger<ProviderModuleLoader>? log = null)
    {
        _log = log ?? NullLogger<ProviderModuleLoader>.Instance;
    }

    /// <summary>
    /// Scans the provided assemblies for <see cref="IProviderModule"/> implementations,
    /// validates each, and calls <see cref="IProviderModule.Register"/> for modules that
    /// pass validation.
    /// </summary>
    /// <param name="services">DI service collection that receives provider registrations.</param>
    /// <param name="registry">Data-source registry passed to each module's Register call.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="assemblies">Assemblies to scan. At least one is required.</param>
    /// <returns>A report describing which modules loaded successfully and which failed.</returns>
    public async Task<ModuleLoadReport> LoadFromAssembliesAsync(
        IServiceCollection services,
        DataSourceRegistry registry,
        CancellationToken ct = default,
        params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(registry);

        if (assemblies is null || assemblies.Length == 0)
            throw new ArgumentException("At least one assembly must be provided.", nameof(assemblies));

        var modules = DiscoverModules(assemblies);
        return await LoadModulesAsync(services, registry, modules, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Validates and loads an explicit set of pre-instantiated module instances.
    /// Use this overload when modules require constructor arguments or when
    /// assembly scanning is not appropriate.
    /// </summary>
    /// <param name="services">DI service collection that receives provider registrations.</param>
    /// <param name="registry">Data-source registry passed to each module's Register call.</param>
    /// <param name="modules">Module instances to load.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A report describing which modules loaded successfully and which failed.</returns>
    public async Task<ModuleLoadReport> LoadModulesAsync(
        IServiceCollection services,
        DataSourceRegistry registry,
        IEnumerable<IProviderModule> modules,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(modules);

        var loaded = new List<LoadedModuleInfo>();
        var failed = new List<FailedModuleInfo>();

        foreach (var module in modules)
        {
            ct.ThrowIfCancellationRequested();

            var moduleId = "(unknown)";
            try
            {
                moduleId = module.ModuleId;

                _log.LogDebug("Validating provider module {ModuleId} ({DisplayName})",
                    moduleId, module.ModuleDisplayName);

                var validation = await module.ValidateAsync(ct).ConfigureAwait(false);

                if (!validation.IsValid)
                {
                    _log.LogWarning(
                        "Provider module {ModuleId} failed validation and will be skipped: {Reason}",
                        moduleId, validation.FailureReason);

                    failed.Add(new FailedModuleInfo(moduleId, module.ModuleDisplayName,
                        validation.FailureReason ?? "Validation returned invalid.", null));
                    continue;
                }

                _log.LogDebug("Registering provider module {ModuleId}", moduleId);
                module.Register(services, registry);

                loaded.Add(new LoadedModuleInfo(moduleId, module.ModuleDisplayName, module.Capabilities));

                _log.LogInformation(
                    "Provider module {ModuleId} ({DisplayName}) loaded — {CapabilityCount} capabilities",
                    moduleId, module.ModuleDisplayName, module.Capabilities.Length);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogError(ex, "Provider module {ModuleId} threw during load and will be skipped", moduleId);
                failed.Add(new FailedModuleInfo(moduleId, "(unknown)", ex.Message, ex));
            }
        }

        var report = new ModuleLoadReport(loaded, failed, loaded.Count + failed.Count);

        _log.LogInformation(
            "Module load complete — {Loaded} loaded, {Failed} failed out of {Total} discovered",
            report.LoadedCount, report.FailedCount, report.TotalDiscovered);

        return report;
    }

    // ------------------------------------------------------------------
    // Private helpers
    // ------------------------------------------------------------------

    private IReadOnlyList<IProviderModule> DiscoverModules(Assembly[] assemblies)
    {
        var modules = new List<IProviderModule>();

        foreach (var assembly in assemblies)
        {
            IEnumerable<Type> types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t is not null)!;
            }

            foreach (var type in types)
            {
                if (type.IsAbstract || type.IsInterface)
                    continue;

                if (!typeof(IProviderModule).IsAssignableFrom(type))
                    continue;

                if (Activator.CreateInstance(type) is IProviderModule module)
                {
                    modules.Add(module);
                    _log.LogDebug("Discovered provider module {Type} ({ModuleId})", type.Name, module.ModuleId);
                }
                else
                {
                    _log.LogWarning(
                        "Type {Type} implements IProviderModule but could not be instantiated via Activator.CreateInstance. " +
                        "Use the LoadModulesAsync overload that accepts pre-built instances.", type.FullName);
                }
            }
        }

        return modules;
    }
}

// -----------------------------------------------------------------------
// Report types
// -----------------------------------------------------------------------

/// <summary>
/// Aggregated result of a <see cref="ProviderModuleLoader"/> load operation.
/// </summary>
/// <param name="Loaded">Modules that passed validation and were registered successfully.</param>
/// <param name="Failed">Modules that were skipped due to validation failure or an exception.</param>
/// <param name="TotalDiscovered">Total number of modules discovered before validation.</param>
public sealed record ModuleLoadReport(
    IReadOnlyList<LoadedModuleInfo> Loaded,
    IReadOnlyList<FailedModuleInfo> Failed,
    int TotalDiscovered)
{
    /// <summary>Number of successfully loaded modules.</summary>
    public int LoadedCount => Loaded.Count;

    /// <summary>Number of modules that failed to load.</summary>
    public int FailedCount => Failed.Count;

    /// <summary>True if all discovered modules loaded without failures.</summary>
    public bool AllLoaded => Failed.Count == 0;

    /// <summary>True if at least one module loaded successfully.</summary>
    public bool AnyLoaded => Loaded.Count > 0;
}

/// <summary>
/// Metadata for a module that was loaded successfully.
/// </summary>
/// <param name="ModuleId">Unique provider identifier (e.g., "alpaca").</param>
/// <param name="DisplayName">Human-readable provider name.</param>
/// <param name="Capabilities">Capabilities declared by the module before registration.</param>
public sealed record LoadedModuleInfo(
    string ModuleId,
    string DisplayName,
    ProviderCapabilities[] Capabilities);

/// <summary>
/// Metadata for a module that could not be loaded.
/// </summary>
/// <param name="ModuleId">Unique provider identifier (e.g., "alpaca").</param>
/// <param name="DisplayName">Human-readable provider name (may be "(unknown)" if the module could not be instantiated).</param>
/// <param name="FailureReason">Short human-readable reason for the failure.</param>
/// <param name="Exception">Exception that caused the failure, or null for validation failures.</param>
public sealed record FailedModuleInfo(
    string ModuleId,
    string DisplayName,
    string FailureReason,
    Exception? Exception);
