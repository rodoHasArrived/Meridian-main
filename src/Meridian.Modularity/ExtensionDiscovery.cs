using System.Reflection;

namespace Meridian.Modularity;

/// <summary>
/// Reflection helpers for discovering extension implementations from assemblies. Centralizes the
/// safe type-loading idiom used by the existing attribute-discovery registries so future
/// extension points do not each re-implement it.
/// </summary>
public static class ExtensionDiscovery
{
    /// <summary>
    /// Returns the loadable types in an assembly, tolerating partially loadable assemblies the
    /// same way the provider discovery path does.
    /// </summary>
    public static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(static t => t is not null)!;
        }
    }

    /// <summary>
    /// Finds concrete (non-abstract, non-interface) types assignable to <typeparamref name="TContract"/>
    /// across the supplied assemblies.
    /// </summary>
    public static IEnumerable<Type> FindImplementations<TContract>(params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        var contract = typeof(TContract);
        foreach (var assembly in assemblies)
        {
            foreach (var type in GetLoadableTypes(assembly))
            {
                if (type.IsAbstract || type.IsInterface)
                    continue;
                if (contract.IsAssignableFrom(type))
                    yield return type;
            }
        }
    }
}
