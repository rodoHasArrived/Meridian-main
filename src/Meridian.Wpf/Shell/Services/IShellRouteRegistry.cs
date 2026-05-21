using Meridian.Wpf.Shell.Models;

namespace Meridian.Wpf.Shell.Services;

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
