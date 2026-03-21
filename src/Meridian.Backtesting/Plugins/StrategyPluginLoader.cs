using System.Reflection;
using System.Runtime.Loader;

namespace Meridian.Backtesting.Plugins;

/// <summary>
/// Loads <see cref="IBacktestStrategy"/> implementations from external assemblies using
/// an isolated <see cref="AssemblyLoadContext"/> so that plugins can be unloaded.
/// </summary>
public sealed class StrategyPluginLoader
{
    /// <summary>
    /// Load all <see cref="IBacktestStrategy"/> types from the assembly at <paramref name="assemblyPath"/>.
    /// </summary>
    public IReadOnlyList<Type> LoadStrategyTypes(string assemblyPath)
    {
        if (!File.Exists(assemblyPath))
            throw new FileNotFoundException($"Strategy assembly not found: {assemblyPath}", assemblyPath);

        var context = new AssemblyLoadContext($"strategy-{Path.GetFileNameWithoutExtension(assemblyPath)}", isCollectible: true);
        var assembly = context.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));

        return assembly.GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface && typeof(IBacktestStrategy).IsAssignableFrom(t))
            .ToList();
    }

    /// <summary>Creates an instance of the strategy type using its default constructor.</summary>
    public IBacktestStrategy Instantiate(Type strategyType)
    {
        var instance = Activator.CreateInstance(strategyType)
            ?? throw new InvalidOperationException($"Cannot instantiate strategy type {strategyType.FullName}");
        return (IBacktestStrategy)instance;
    }

    /// <summary>Returns [StrategyParameter]-decorated properties on the strategy type.</summary>
    public IReadOnlyList<StrategyParameterInfo> GetParameters(Type strategyType) =>
        strategyType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => new { prop = p, attr = p.GetCustomAttribute<StrategyParameterAttribute>() })
            .Where(x => x.attr is not null)
            .Select(x => new StrategyParameterInfo(x.prop.Name, x.attr!.DisplayName, x.attr.Description, x.prop.PropertyType))
            .ToList();
}

/// <summary>Metadata for a discoverable strategy parameter.</summary>
public sealed record StrategyParameterInfo(
    string PropertyName,
    string DisplayName,
    string? Description,
    Type ParameterType);
