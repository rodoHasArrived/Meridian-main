using Meridian.Wpf.Shell.Models;

namespace Meridian.Wpf.Shell.Services;

/// <summary>
/// Resolves canonical page tags, page types, aliases, and workspace membership for desktop routes.
/// </summary>
public interface IShellRouteRegistry
{
    IReadOnlyList<ShellRoute> Routes { get; }

    ShellRoute? GetRoute(string? pageTag);

    bool IsRegistered(string? pageTag);

    Type? GetPageType(string? pageTag);

    string GetCanonicalPageTag(string? pageTag);

    string? InferWorkspaceIdForPageTag(string? pageTag);

    IReadOnlyList<ShellRoute> GetRoutesForWorkspace(string workspaceId);
}
