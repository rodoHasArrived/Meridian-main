namespace Meridian.Mcp.Services;

/// <summary>
/// Locates the Meridian repository root and exposes well-known paths
/// used by tools and resources.
/// </summary>
public sealed class RepoPathService
{
    private const string SolutionFileName = "Meridian.sln";
    private const string EnvVar = "MDC_REPO_ROOT";

    public string Root { get; }
    public string DocsPath => Path.Combine(Root, "docs");
    public string AdrPath => Path.Combine(Root, "docs", "adr");
    public string AiDocsPath => Path.Combine(Root, "docs", "ai");
    public string GithubPath => Path.Combine(Root, ".github");
    public string InstructionsPath => Path.Combine(Root, ".github", "instructions");
    public string AgentsPath => Path.Combine(Root, ".github", "agents");
    public string AiClaudePath => Path.Combine(Root, "docs", "ai", "claude");
    public string TemplatesPath => Path.Combine(Root, "docs", "examples", "provider-template");
    public string AdaptersPath => Path.Combine(Root, "src", "Meridian.Infrastructure", "Adapters");
    public string AuditScriptPath => Path.Combine(Root, "build", "scripts", "ai-repo-updater.py");
    public string AiEditToolPath => Path.Combine(Root, "build", "scripts", "ai", "ai-edit-tool.py");

    public RepoPathService()
    {
        Root = Discover();
    }

    private static string Discover()
    {
        var fromEnv = Environment.GetEnvironmentVariable(EnvVar);
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            if (!File.Exists(Path.Combine(fromEnv, SolutionFileName)))
                throw new InvalidOperationException(
                    $"MDC_REPO_ROOT='{fromEnv}' does not contain '{SolutionFileName}'.");
            // A trailing separator would otherwise make the boundary prefix "root//" and
            // reject every path inside the repository.
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(fromEnv));
        }

        // Walk up from the executable's location
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, SolutionFileName)))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Cannot locate '{SolutionFileName}'. " +
            $"Set the {EnvVar} environment variable to the repository root.");
    }

    public string KnownErrorsFile => Path.Combine(AiDocsPath, "ai-known-errors.md");
    public string CSharpInstructionsFile => Path.Combine(InstructionsPath, "csharp.instructions.md");
    public string CodeReviewAgentFile => Path.Combine(AgentsPath, "code-review-agent.md");
    public string ProvidersGuideFile => Path.Combine(AiClaudePath, "CLAUDE.providers.md");
    public string TestingGuideFile => Path.Combine(AiClaudePath, "CLAUDE.testing.md");
    public string StorageGuideFile => Path.Combine(AiClaudePath, "CLAUDE.storage.md");
    public string FSharpGuideFile => Path.Combine(AiClaudePath, "CLAUDE.fsharp.md");

    public string ResolveInsideRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path is required.", nameof(path));

        var fullPath = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(Root, path));
        var rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Root));

        // Compare the paths the file system will actually open: a symlink inside the
        // repository can point anywhere, so resolve links on both sides first.
        var realPath = ResolveLinks(fullPath);
        var realRoot = ResolveLinks(rootPath);
        if (!IsSameOrInside(realPath, realRoot) || !IsSameOrInside(fullPath, rootPath))
        {
            throw new InvalidOperationException($"Path is outside the Meridian repository root: {path}");
        }

        return fullPath;
    }

    // Case-insensitive only where the file system usually is; on Linux "/repo" and "/REPO"
    // are different directories, so an ignore-case prefix check would admit a sibling.
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private static bool IsSameOrInside(string candidate, string root) =>
        string.Equals(candidate, root, PathComparison) ||
        candidate.StartsWith(root + Path.DirectorySeparatorChar, PathComparison);

    /// <summary>
    /// Resolves symbolic links on every existing component of <paramref name="fullPath"/>.
    /// Components that do not exist yet (for example a file about to be created) are kept as-is.
    /// </summary>
    internal static string ResolveLinks(string fullPath)
    {
        var root = Path.GetPathRoot(fullPath) ?? string.Empty;
        var current = root;
        var parts = fullPath[root.Length..].Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i < parts.Length; i++)
        {
            var next = Path.Combine(current, parts[i]);
            FileSystemInfo info = Directory.Exists(next) ? new DirectoryInfo(next) : new FileInfo(next);
            if (!info.Exists)
            {
                // Nothing further can be a link; append the remaining components unchanged.
                return Path.Combine([current, .. parts[i..]]);
            }

            if (info.LinkTarget is not null)
            {
                var target = info.ResolveLinkTarget(returnFinalTarget: true);
                next = target is null ? next : Path.GetFullPath(target.FullName);
            }

            current = next;
        }

        return Path.TrimEndingDirectorySeparator(current);
    }
}
