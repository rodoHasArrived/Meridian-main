using System.Reflection;
using Meridian.Infrastructure.Adapters.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Infrastructure.DataSources;

/// <summary>
/// Registry for discovering and registering data source providers.
/// </summary>
public sealed class DataSourceRegistry
{
    private readonly List<DataSourceMetadata> _sources = new();
    private readonly Dictionary<string, ProviderModuleContext> _moduleContexts
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the discovered data source metadata entries.
    /// </summary>
    public IReadOnlyList<DataSourceMetadata> Sources => _sources;

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
        }
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
                catch
                {
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
                catch
                {
                    continue;
                }
            }
        }
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }
}
