using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Infrastructure.DataSources;

/// <summary>
/// Registry for discovering and registering data source providers.
/// </summary>
public sealed class DataSourceRegistry
{
    private readonly List<DataSourceMetadata> _sources = new();

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
    /// Discovers provider modules and executes their registrations.
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
                {
                    continue;
                }

                if (!typeof(Meridian.Infrastructure.Adapters.Core.IProviderModule).IsAssignableFrom(type))
                {
                    continue;
                }

                if (Activator.CreateInstance(type) is Meridian.Infrastructure.Adapters.Core.IProviderModule module)
                {
                    module.Register(services, this);
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
