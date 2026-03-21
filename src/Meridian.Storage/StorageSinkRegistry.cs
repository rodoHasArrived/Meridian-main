using System.Reflection;
using Meridian.Storage.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Storage;

/// <summary>
/// Registry for discovering and registering <see cref="IStorageSink"/> plugins
/// that are decorated with <see cref="StorageSinkAttribute"/>.
/// </summary>
/// <remarks>
/// Mirrors the <c>DataSourceRegistry</c> pattern from the provider layer (ADR-005).
/// Usage in the composition root:
/// <code>
/// var registry = new StorageSinkRegistry();
/// registry.DiscoverFromAssemblies(typeof(JsonlStorageSink).Assembly);
/// // registry.Sinks now contains JsonlStorageSink, ParquetStorageSink, etc.
/// </code>
/// </remarks>
public sealed class StorageSinkRegistry
{
    private readonly List<StorageSinkMetadata> _sinks = new();

    /// <summary>
    /// Gets the discovered storage sink metadata entries, in the order they were found.
    /// </summary>
    public IReadOnlyList<StorageSinkMetadata> Sinks => _sinks;

    /// <summary>
    /// Scans the provided assemblies for concrete types decorated with
    /// <see cref="StorageSinkAttribute"/> that implement <see cref="IStorageSink"/>,
    /// and adds their metadata to the registry.
    /// </summary>
    /// <remarks>
    /// Duplicate IDs (case-insensitive) are silently ignored so that scanning the
    /// same assembly twice is safe.
    /// </remarks>
    /// <param name="assemblies">One or more assemblies to scan.</param>
    /// <exception cref="ArgumentException">Thrown when no assemblies are supplied.</exception>
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
                if (!type.IsStorageSinkPlugin())
                {
                    continue;
                }

                var metadata = type.GetStorageSinkMetadata();
                if (metadata is not null
                    && _sinks.All(s => !s.Id.Equals(metadata.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    _sinks.Add(metadata);
                }
            }
        }
    }

    /// <summary>
    /// Registers all discovered sink implementation types into the service collection
    /// so that they can be resolved by <see cref="IServiceProvider"/> during composition.
    /// </summary>
    /// <remarks>
    /// Only registers types that are not already present in the collection, so calling
    /// this method after explicit registrations is safe.
    /// </remarks>
    /// <param name="services">The DI service collection to populate.</param>
    /// <param name="lifetime">Service lifetime for registered sinks. Defaults to singleton.</param>
    public void RegisterServices(
        IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
    {
        ArgumentNullException.ThrowIfNull(services);

        foreach (var sink in _sinks)
        {
            // Skip if the concrete type is already registered (e.g., via explicit registration).
            if (services.Any(d => d.ServiceType == sink.ImplementationType))
            {
                continue;
            }

            services.Add(new ServiceDescriptor(sink.ImplementationType, sink.ImplementationType, lifetime));
        }
    }

    /// <summary>
    /// Attempts to find a registered sink by its identifier (case-insensitive).
    /// </summary>
    /// <param name="id">The sink identifier to look up (e.g., "jsonl", "parquet").</param>
    /// <param name="metadata">
    /// When this method returns <c>true</c>, contains the sink metadata; otherwise <c>null</c>.
    /// </param>
    /// <returns><c>true</c> if a matching sink was found; otherwise <c>false</c>.</returns>
    public bool TryGetSink(string id, out StorageSinkMetadata? metadata)
    {
        metadata = _sinks.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        return metadata is not null;
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
