namespace Meridian;

/// <summary>
/// Resolves the browser-workstation asset tree the host serves (PRD-018). The canonical tree is
/// the tracked bundle at <c>src/Meridian.Ui/wwwroot/workstation</c> — the same tree the dashboard
/// build writes, the publish/install lanes copy, and the docs name — so the repo-launch,
/// published, and installed lanes all serve one verified bundle instead of resolving whatever
/// happens to sit under the launch directory.
/// </summary>
public static class WorkstationAssetTree
{
    /// <summary>Repo-relative canonical source tree for the built workstation bundle.</summary>
    public const string CanonicalSourceTree = "src/Meridian.Ui/wwwroot/workstation";

    /// <summary>The exact command that regenerates the canonical bundle from dashboard source.</summary>
    public const string BuildRemediationCommand = "npm --prefix src/Meridian.Ui/dashboard run build";

    private const int MaxUpwardProbeDepth = 8;

    /// <summary>
    /// Resolves the web root (the directory whose <c>workstation/index.html</c> the host serves),
    /// independent of launch directory. Probe order: the launch directory itself (installed
    /// layout, where the bundle sits beside the executable's working directory), the executable
    /// directory (published output carries the bundle as copied content), then the repository
    /// checkout discovered upward from either location. Returns <c>null</c> when no tree carries
    /// a workstation bundle.
    /// </summary>
    public static string? ResolveWebRoot(string launchDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(launchDirectory);

        foreach (var candidate in EnumerateCandidateWebRoots(launchDirectory))
        {
            if (File.Exists(Path.Combine(candidate, "workstation", "index.html")))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    /// <summary>
    /// Operator-facing description of a missing bundle, naming the canonical tree and the exact
    /// remediation command so a failed readiness check is self-explanatory.
    /// </summary>
    public static string DescribeUnavailable()
        => $"Workstation bundle is unavailable. Build the canonical tree ({CanonicalSourceTree}) with " +
           $"'{BuildRemediationCommand}', or launch a published layout that carries wwwroot/workstation " +
           "beside the executable.";

    private static IEnumerable<string> EnumerateCandidateWebRoots(string launchDirectory)
    {
        yield return Path.Combine(launchDirectory, "wwwroot");
        yield return Path.Combine(AppContext.BaseDirectory, "wwwroot");

        foreach (var repoRoot in EnumerateAncestorRepositoryRoots(launchDirectory))
        {
            yield return Path.Combine(repoRoot, "src", "Meridian.Ui", "wwwroot");
        }

        foreach (var repoRoot in EnumerateAncestorRepositoryRoots(AppContext.BaseDirectory))
        {
            yield return Path.Combine(repoRoot, "src", "Meridian.Ui", "wwwroot");
        }
    }

    private static IEnumerable<string> EnumerateAncestorRepositoryRoots(string start)
    {
        var current = TryCreateDirectory(start);
        for (var depth = 0; current is not null && depth < MaxUpwardProbeDepth; depth++)
        {
            if (File.Exists(Path.Combine(current.FullName, "Meridian.sln")))
            {
                yield return current.FullName;
                yield break;
            }

            current = current.Parent;
        }
    }

    private static DirectoryInfo? TryCreateDirectory(string path)
    {
        try
        {
            return new DirectoryInfo(Path.GetFullPath(path));
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException or System.Security.SecurityException)
        {
            return null;
        }
    }
}
