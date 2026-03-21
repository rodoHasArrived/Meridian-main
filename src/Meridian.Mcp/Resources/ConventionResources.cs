namespace Meridian.Mcp.Resources;

[McpServerResourceType]
public sealed class ConventionResources(RepoPathService repo)
{
    [McpServerResource(UriTemplate = "mdc://conventions", Name = "coding_conventions",
        Title = "C# Coding Conventions",
        MimeType = "text/markdown")]
    [Description("Full C# coding conventions for the Meridian project.")]
    public string GetConventions()
    {
        if (File.Exists(repo.CSharpInstructionsFile))
            return File.ReadAllText(repo.CSharpInstructionsFile);

        return "# C# Conventions\n\n" +
               "Conventions file not found at `.github/instructions/csharp.instructions.md`.\n\n" +
               "Use the `get_coding_conventions` tool for embedded convention rules.";
    }

    [McpServerResource(UriTemplate = "mdc://known-errors", Name = "known_errors",
        Title = "AI Known Error Registry",
        MimeType = "text/markdown")]
    [Description("Full registry of known AI agent error patterns with prevention checklists.")]
    public string GetKnownErrors()
    {
        if (File.Exists(repo.KnownErrorsFile))
            return File.ReadAllText(repo.KnownErrorsFile);

        return "# AI Known Errors\n\nKnown errors file not found at `docs/ai/ai-known-errors.md`.";
    }

    [McpServerResource(UriTemplate = "mdc://guides/{name}", Name = "dev_guide",
        Title = "Developer Guide",
        MimeType = "text/markdown")]
    [Description("Developer guides. name: providers | testing | storage | fsharp | code-review | provider-implementation")]
    public string GetGuide(string name)
    {
        var fileMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["providers"] = repo.ProvidersGuideFile,
            ["testing"] = repo.TestingGuideFile,
            ["storage"] = repo.StorageGuideFile,
            ["fsharp"] = repo.FSharpGuideFile,
            ["code-review"] = repo.CodeReviewAgentFile,
            ["provider-implementation"] = Path.Combine(repo.DocsPath, "development", "provider-implementation.md"),
        };

        if (!fileMap.TryGetValue(name, out var path))
            return $"Unknown guide '{name}'. Available: {string.Join(", ", fileMap.Keys)}";

        return File.Exists(path) ? File.ReadAllText(path) : $"Guide file not found: {path}";
    }
}
