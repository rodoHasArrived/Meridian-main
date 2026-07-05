using System.Reflection;
using Meridian.Infrastructure.Adapters.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Infrastructure.DataSources;

/// <summary>
/// A discovery or registration failure captured during data source and module scanning.
/// </summary>
/// <param name="Stage">Where the failure occurred: "type-load", "activate", or "register".</param>
/// <param name="Subject">The type or assembly the failure relates to.</param>
/// <param name="ModuleId">Module ID when known (register stage), otherwise null.</param>
/// <param name="ErrorType">Exception type name.</param>
/// <param name="ErrorMessage">Exception message.</param>
public sealed record DataSourceDiscoveryFailure(
    string Stage,
    string Subject,
    string? ModuleId,
    string ErrorType,
    string ErrorMessage);

/// <summary>
/// Registry for discovering and registering data source providers.
/// </summary>
public sealed class DataSourceRegistry
{
    private readonly List<DataSourceMetadata> _sources = new();
    private readonly List<DataSourceDiscoveryFailure> _failures = new();
    private readonly Dictionary<string, ProviderModuleContext> _moduleContexts
        = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger _log;

    public DataSourceRegistry(ILogger? log = null)
    {
        _log = log ?? NullLogger.Instance;
    }

    /// <summary>
    /// Gets the discovered data source metadata entries.
    /// </summary>
    public IReadOnlyList<DataSourceMetadata> Sources => _sources;

    /// <summary>
    /// Failures captured while scanning, activating, or registering modules.
    /// Empty when everything registered cleanly. Scanning continues past
    /// individual failures so one broken module cannot block the rest.
    /// </summary>
    public IReadOnlyList<DataSourceDiscoveryFailure> Failures => _failures;

    /// <summary>
    /// Discover data sources from the provided assemblies.
    /// </summary>
    public void DiscoverFromAssemblies(params Assembly[] assemblies)
    {
        if (assemblies is null || assemblies.Length == 0)
        {
            throw new ArgumentException("At least one assembly must be provided.", nameof(assemblies));
        }

        foreach (var assembly in assemblies)
        {
            var types = GetLoadableTypes(assembly);
            foreach (var type in types)
            {
                if (!type.IsDataSource())
                {
                    continue;
                }

                var metadata = type.GetDataSourceMetadata();
                if (metadata is not null && _sources.All(s => !s.Id.Equals(metadata.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    _sources.Add(metadata);
                }
            }
        }
    }

    /// <summary>
    /// Registers discovered data sources into the service collection.
    /// </summary>
    public void RegisterServices(IServiceCollection services, ServiceLifetime lifetime = ServiceLifetime.Singleton)
    {
        ArgumentNullException.ThrowIfNull(services);

        foreach (var source in _sources)
        {
            services.Add(new ServiceDescriptor(source.ImplementationType, source.ImplementationType, lifetime));
            // Only register as IDataSource if the type actually implements it
            if (typeof(IDataSource).IsAssignableFrom(source.ImplementationType))
            {
                services.Add(new ServiceDescriptor(typeof(IDataSource),
                    sp => (IDataSource)sp.GetRequiredService(source.ImplementationType),
                    lifetime));
            }

            RegisterProviderInterface(
                services,
                source.ImplementationType,
                "Meridian.Infrastructure.Adapters.Core.ICorporateActionProvider",
                lifetime);
        }
    }

    private static void RegisterProviderInterface(
        IServiceCollection services,
        Type implementationType,
        string serviceTypeFullName,
        ServiceLifetime lifetime)
    {
        var serviceType = implementationType
            .GetInterfaces()
            .FirstOrDefault(type => string.Equals(type.FullName, serviceTypeFullName, StringComparison.Ordinal));
        if (serviceType is null)
            return;

        services.Add(new ServiceDescriptor(
            serviceType,
            sp => sp.GetRequiredService(implementationType),
            lifetime));
    }

    /// <summary>
    /// Pre-configures a module with a <see cref="ProviderModuleContext"/> that will be
    /// injected via <see cref="IProviderModule.Configure"/> before
    /// <see cref="IProviderModule.Register"/> is called.
    /// </summary>
    /// <param name="moduleId">Module ID matching <see cref="IProviderModule.ModuleId"/> (case-insensitive).</param>
    /// <param name="context">The resolved context to inject.</param>
    public DataSourceRegistry ConfigureModule(string moduleId, ProviderModuleContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        ArgumentNullException.ThrowIfNull(context);
        _moduleContexts[moduleId] = context;
        return this;
    }

    /// <summary>
    /// Pre-configures multiple modules at once from a dictionary keyed by module ID.
    /// </summary>
    public DataSourceRegistry ConfigureModules(IReadOnlyDictionary<string, ProviderModuleContext> contexts)
    {
        ArgumentNullException.ThrowIfNull(contexts);
        foreach (var (id, ctx) in contexts)
            _moduleContexts[id] = ctx;
        return this;
    }

    /// <summary>
    /// Discovers provider modules and executes their registrations.
    /// When a matching <see cref="ProviderModuleContext"/> exists (registered via
    /// <see cref="ConfigureModule"/>), it is injected before <see cref="IProviderModule.Register"/>.
    /// Modules with <see cref="IProviderModule.RequiresExternalConfig"/> set to true are
    /// skipped when no context was registered for their ID.
    /// </summary>
    public void RegisterModules(IServiceCollection services, params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(services);

        foreach (var assembly in assemblies)
        {
            var types = GetLoadableTypes(assembly);
            foreach (var type in types)
            {
                if (type.IsAbstract || type.IsInterface)
                    continue;

                if (!typeof(IProviderModule).IsAssignableFrom(type))
                    continue;

                if (type.GetConstructor(Type.EmptyTypes) is null)
                    continue;

                IProviderModule module;
                try
                {
                    if (Activator.CreateInstance(type) is not IProviderModule m)
                        continue;
                    module = m;
                }
                catch (Exception ex)
                {
                    RecordFailure("activate", type.FullName ?? type.Name, moduleId: null, ex);
                    continue;
                }

                var moduleId = module.ModuleId;

                if (_moduleContexts.TryGetValue(moduleId, out var context))
                {
                    if (!context.Enabled)
                        continue;

                    module.Configure(context);
                }
                else if (module.RequiresExternalConfig)
                {
                    continue;
                }

                try
                {
                    module.Register(services, this);
                }
                catch (Exception ex)
                {
                    RecordFailure("register", type.FullName ?? type.Name, moduleId, ex);
                    continue;
                }
            }
        }
    }

    private IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            var assemblyName = assembly.GetName().Name ?? assembly.FullName ?? "unknown";
            foreach (var loaderError in ex.LoaderExceptions)
            {
                if (loaderError is not null)
                    RecordFailure("type-load", assemblyName, moduleId: null, loaderError);
            }

            return ex.Types.Where(t => t is not null)!;
        }
    }

    private void RecordFailure(string stage, string subject, string? moduleId, Exception error)
    {
        _failures.Add(new DataSourceDiscoveryFailure(
            stage, subject, moduleId, error.GetType().Name, error.Message));
        _log.LogWarning(
            error,
            "Data source {Stage} failure for {Subject} (module {ModuleId})",
            stage, subject, moduleId ?? "n/a");
    }
}
