namespace Meridian.Mcp.Tools;

[McpServerToolType]
public sealed class KnownErrorTools(RepoPathService repo)
{
    [McpServerTool(Name = "get_known_errors")]
    [Description("Get AI known error patterns from the project registry. Optionally filter by area: docs, build, tests, runtime, config, CI, WPF, process.")]
    public string GetKnownErrors(
        [Description("Optional area filter: docs | build | tests | runtime | config | CI | WPF | process")] string? area = null)
    {
        var content = ReadKnownErrors();
        if (content is null)
            return $"Known errors file not found at: {repo.KnownErrorsFile}";

        if (string.IsNullOrWhiteSpace(area))
            return content;

        // Filter sections by area
        var filtered = FilterByArea(content, area);
        return string.IsNullOrWhiteSpace(filtered)
            ? $"No known errors found for area '{area}'. Available areas: docs, build, tests, runtime, config, CI, WPF, process."
            : filtered;
    }

    [McpServerTool(Name = "search_known_errors")]
    [Description("Search the known error registry by keyword. Returns entries where the keyword appears in symptoms, root cause, or prevention checklist.")]
    public string SearchKnownErrors(
        [Description("Search keyword, e.g. 'CancellationToken', 'HttpClient', 'sealed'")] string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return "Provide a non-empty search keyword.";

        var content = ReadKnownErrors();
        if (content is null)
            return $"Known errors file not found at: {repo.KnownErrorsFile}";

        var sections = SplitIntoEntries(content);
        var matched = sections
            .Where(s => s.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matched.Count == 0)
            return $"No known error entries match '{query}'.";

        var sb = new StringBuilder();
        sb.AppendLine($"## Known Errors matching '{query}' ({matched.Count} found)\n");
        foreach (var entry in matched)
            sb.AppendLine(entry).AppendLine("---");
        return sb.ToString();
    }

    private string? ReadKnownErrors()
    {
        if (!File.Exists(repo.KnownErrorsFile))
            return null;
        return File.ReadAllText(repo.KnownErrorsFile);
    }

    private static string FilterByArea(string content, string area)
    {
        var entries = SplitIntoEntries(content);
        var matched = entries
            .Where(e => e.Contains($"**Area**: {area}", StringComparison.OrdinalIgnoreCase) ||
                        e.Contains($"Area**: {area}", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matched.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine($"## Known Errors — Area: {area} ({matched.Count} found)\n");
        foreach (var entry in matched)
            sb.AppendLine(entry).AppendLine("---");
        return sb.ToString();
    }

    private static List<string> SplitIntoEntries(string content)
    {
        // Each entry starts with "### AI-"
        var lines = content.Split('\n');
        var entries = new List<string>();
        var current = new StringBuilder();

        foreach (var line in lines)
        {
            if (line.StartsWith("### AI-") && current.Length > 0)
            {
                entries.Add(current.ToString().Trim());
                current.Clear();
            }
            current.AppendLine(line);
        }
        if (current.Length > 0)
            entries.Add(current.ToString().Trim());

        return entries.Where(e => e.StartsWith("### AI-")).ToList();
    }
}
