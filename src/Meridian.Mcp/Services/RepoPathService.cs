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
            return fromEnv;
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
}
