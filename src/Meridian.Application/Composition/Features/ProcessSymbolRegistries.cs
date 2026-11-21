using System.Runtime.CompilerServices;
using Meridian.Storage.Interfaces;
using Meridian.Storage.Services;

namespace Meridian.Application.Composition.Features;

// Installed desktop hosts the UI server and collector in separate containers. Key by resolved
// root so the operator's durable mapping edits and resolver cache are shared by both.
internal static class ProcessSymbolRegistries
{
    private static readonly Dictionary<string, WeakReference<SymbolRegistryService>> Stores =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private static readonly ConditionalWeakTable<ISymbolRegistryService, CanonicalSymbolRegistry> Resolvers = new();

    internal static SymbolRegistryService ForStorageRoot(string storageRoot)
    {
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(storageRoot));
        lock (Stores)
        {
            if (Stores.TryGetValue(path, out var existing) && existing.TryGetTarget(out var store))
                return store;

            var created = new SymbolRegistryService(path);
            Stores[path] = new WeakReference<SymbolRegistryService>(created);
            return created;
        }
    }

    internal static CanonicalSymbolRegistry ResolverFor(ISymbolRegistryService store)
        => Resolvers.GetValue(store, static value => new CanonicalSymbolRegistry(value));
}
