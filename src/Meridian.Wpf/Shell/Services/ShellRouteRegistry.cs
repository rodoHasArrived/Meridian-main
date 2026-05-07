using Meridian.Wpf.Models;
using Meridian.Wpf.Shell.Models;

namespace Meridian.Wpf.Shell.Services;

public sealed class ShellRouteRegistry : IShellRouteRegistry
{
    private readonly Lazy<IReadOnlyList<ShellRoute>> _routes = new(BuildRoutes);
    private readonly Lazy<IReadOnlyDictionary<string, ShellRoute>> _routesByTag = new(BuildLookup);

    public IReadOnlyList<ShellRoute> Routes => _routes.Value;

    public ShellRoute? GetRoute(string? pageTag)
    {
        if (string.IsNullOrWhiteSpace(pageTag))
        {
            return null;
        }

        return _routesByTag.Value.TryGetValue(pageTag.Trim(), out var route)
            ? route
            : null;
    }

    public bool IsRegistered(string? pageTag) => GetRoute(pageTag) is not null;

    public Type? GetPageType(string? pageTag) => GetRoute(pageTag)?.PageType;

    public string GetCanonicalPageTag(string? pageTag)
        => GetRoute(pageTag)?.PageTag ?? pageTag?.Trim() ?? string.Empty;

    public string? InferWorkspaceIdForPageTag(string? pageTag) => GetRoute(pageTag)?.WorkspaceId;

    public IReadOnlyList<ShellRoute> GetRoutesForWorkspace(string workspaceId)
    {
        if (string.IsNullOrWhiteSpace(workspaceId))
        {
            return Array.Empty<ShellRoute>();
        }

        return Routes
            .Where(route => string.Equals(route.WorkspaceId, workspaceId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(route => route.VisibilityTier)
            .ThenBy(route => route.Order)
            .ThenBy(route => route.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<ShellRoute> BuildRoutes()
        => ShellNavigationCatalog.Pages
            .Select(ShellRoute.FromDescriptor)
            .ToArray();

    private static IReadOnlyDictionary<string, ShellRoute> BuildLookup()
    {
        var lookup = new Dictionary<string, ShellRoute>(StringComparer.OrdinalIgnoreCase);
        foreach (var route in BuildRoutes())
        {
            Register(lookup, route.PageTag, route);
            foreach (var alias in route.Aliases)
            {
                Register(lookup, alias, route);
            }
        }

        return lookup;
    }

    private static void Register(IDictionary<string, ShellRoute> lookup, string tag, ShellRoute route)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return;
        }

        if (lookup.TryGetValue(tag, out var existing) &&
            !string.Equals(existing.PageTag, route.PageTag, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Shell route tag '{tag}' is already registered for '{existing.PageTag}' and cannot also point to '{route.PageTag}'.");
        }

        lookup[tag] = route;
    }
}
