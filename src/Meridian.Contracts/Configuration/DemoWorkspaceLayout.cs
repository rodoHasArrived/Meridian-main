namespace Meridian.Contracts.Configuration;

/// <summary>
/// Shared, host-agnostic rules for the isolated Meridian demo workspace that the
/// <c>--seed-demo</c> / <c>--reset-demo</c> CLI verbs and the <c>--demo</c> serve mode operate on.
/// </summary>
/// <remarks>
/// <para>
/// The demo workspace is a dedicated, clearly-named data root nested one level under the active
/// data root (<c>{dataRoot}/demo-workspace</c>). Every seeded, durable store is rooted here, so
/// seeding can never write into — and <c>--reset-demo</c> can never delete — a non-demo root.
/// </para>
/// <para>
/// This type is intentionally dependency-free and pure so the same isolation and guard rules are
/// enforced identically by the host CLI, the shared startup bootstrapper, the seeder, and tests.
/// </para>
/// </remarks>
public static class DemoWorkspaceLayout
{
    /// <summary>The dedicated folder name for the demo workspace, nested under the active data root.</summary>
    public const string DemoWorkspaceFolderName = "demo-workspace";

    /// <summary>
    /// Marker file written into the demo root at seed time. <see cref="EnsureSafeToDelete"/> refuses
    /// to tear down a directory that does not carry it, so <c>--reset-demo</c> can only ever remove a
    /// workspace Meridian itself seeded.
    /// </summary>
    public const string SentinelFileName = ".meridian-demo-workspace.json";

    /// <summary>Environment variable that activates demo mode for the serving host.</summary>
    public const string DemoModeEnvironmentVariable = "MERIDIAN_DEMO";

    /// <summary>Seeds the isolated demo workspace (and, unless <see cref="SeedOnlyFlag"/> is set, serves it).</summary>
    public const string SeedDemoFlag = "--seed-demo";

    /// <summary>Tears down the isolated demo workspace only.</summary>
    public const string ResetDemoFlag = "--reset-demo";

    /// <summary>Serves an already-seeded demo workspace by redirecting the active data root to the demo root.</summary>
    public const string DemoFlag = "--demo";

    /// <summary>Seeds without continuing to serve — used by CI and scripted seeding.</summary>
    public const string SeedOnlyFlag = "--seed-only";

    /// <summary>
    /// Resolves the absolute demo workspace root nested under <paramref name="baseDataRoot"/>.
    /// </summary>
    public static string ResolveDemoRoot(string baseDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDataRoot);
        return Path.GetFullPath(Path.Combine(baseDataRoot, DemoWorkspaceFolderName));
    }

    /// <summary>Absolute path of the sentinel marker inside a demo root.</summary>
    public static string ResolveSentinelPath(string demoRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(demoRoot);
        return Path.Combine(demoRoot, SentinelFileName);
    }

    /// <summary>Returns true when <paramref name="flag"/> is present (case-insensitive).</summary>
    public static bool HasFlag(IReadOnlyList<string> args, string flag)
    {
        ArgumentNullException.ThrowIfNull(args);
        for (var i = 0; i < args.Count; i++)
        {
            if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True when the CLI is asking to seed the demo workspace.</summary>
    public static bool HasSeedDemoFlag(IReadOnlyList<string> args) => HasFlag(args, SeedDemoFlag);

    /// <summary>True when the CLI is asking to tear down the demo workspace.</summary>
    public static bool HasResetDemoFlag(IReadOnlyList<string> args) => HasFlag(args, ResetDemoFlag);

    /// <summary>True when seeding should stop before serving (CI / scripted use).</summary>
    public static bool HasSeedOnlyFlag(IReadOnlyList<string> args) => HasFlag(args, SeedOnlyFlag);

    /// <summary>
    /// True when the serving host should redirect its data root to the isolated demo root. This is
    /// requested explicitly with <c>--demo</c> / <c>--seed-demo</c>, or ambiently via the
    /// <c>MERIDIAN_DEMO</c> environment variable.
    /// </summary>
    public static bool IsDemoModeRequested(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (HasFlag(args, DemoFlag) || HasFlag(args, SeedDemoFlag))
        {
            return true;
        }

        var envValue = Environment.GetEnvironmentVariable(DemoModeEnvironmentVariable);
        return IsTruthy(envValue);
    }

    private static bool IsTruthy(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        return trimmed.Equals("1", StringComparison.Ordinal)
            || trimmed.Equals("true", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("on", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Validates that <paramref name="candidate"/> is the isolated demo root derived from
    /// <paramref name="baseDataRoot"/> — a distinct, correctly-named direct child. Throws
    /// <see cref="DemoWorkspaceIsolationException"/> otherwise. This is the structural half of the
    /// teardown guard and never touches the filesystem, so it is safe to call before the directory
    /// exists.
    /// </summary>
    public static string EnsureIsolatedDemoRoot(string candidate, string baseDataRoot)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            throw new DemoWorkspaceIsolationException("Demo workspace path is empty.");
        }

        if (string.IsNullOrWhiteSpace(baseDataRoot))
        {
            throw new DemoWorkspaceIsolationException("Base data root is empty; refusing to resolve a demo workspace.");
        }

        var normalizedCandidate = NormalizeDirectory(candidate);
        var normalizedBase = NormalizeDirectory(baseDataRoot);

        if (string.Equals(normalizedCandidate, normalizedBase, PathComparison))
        {
            throw new DemoWorkspaceIsolationException(
                $"Refusing to treat the active data root as a demo workspace: '{normalizedCandidate}'.");
        }

        var name = Path.GetFileName(normalizedCandidate);
        if (!string.Equals(name, DemoWorkspaceFolderName, StringComparison.OrdinalIgnoreCase))
        {
            throw new DemoWorkspaceIsolationException(
                $"Demo workspace must be named '{DemoWorkspaceFolderName}', but was '{name}' ('{normalizedCandidate}').");
        }

        var parent = Path.GetDirectoryName(normalizedCandidate);
        if (string.IsNullOrEmpty(parent))
        {
            throw new DemoWorkspaceIsolationException(
                $"Demo workspace '{normalizedCandidate}' has no parent directory; refusing to operate on a filesystem root.");
        }

        if (!string.Equals(NormalizeDirectory(parent), normalizedBase, PathComparison))
        {
            throw new DemoWorkspaceIsolationException(
                $"Demo workspace '{normalizedCandidate}' is not a direct child of the active data root '{normalizedBase}'.");
        }

        return normalizedCandidate;
    }

    /// <summary>
    /// Validates that <paramref name="candidate"/> is safe to delete: it must pass
    /// <see cref="EnsureIsolatedDemoRoot"/> and carry the demo sentinel marker. Returns the validated
    /// absolute path. Throws <see cref="DemoWorkspaceIsolationException"/> if the guard fails.
    /// </summary>
    public static string EnsureSafeToDelete(string candidate, string baseDataRoot)
    {
        var normalizedCandidate = EnsureIsolatedDemoRoot(candidate, baseDataRoot);

        if (!Directory.Exists(normalizedCandidate))
        {
            // Nothing to delete is not an isolation failure; callers treat this as a no-op.
            return normalizedCandidate;
        }

        var sentinel = ResolveSentinelPath(normalizedCandidate);
        if (!File.Exists(sentinel))
        {
            throw new DemoWorkspaceIsolationException(
                $"Directory '{normalizedCandidate}' is missing the demo sentinel '{SentinelFileName}'; refusing to delete a directory Meridian did not seed as a demo workspace.");
        }

        return normalizedCandidate;
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static string NormalizeDirectory(string path)
    {
        var full = Path.GetFullPath(path);
        if (full.Length > 1)
        {
            full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        return full;
    }
}

/// <summary>
/// Raised when a demo-workspace path fails the isolation guard — i.e. it is not the dedicated,
/// correctly-named demo root derived from the active data root, or lacks the demo sentinel.
/// </summary>
public sealed class DemoWorkspaceIsolationException : Exception
{
    public DemoWorkspaceIsolationException(string message) : base(message)
    {
    }
}
