namespace Meridian.Mcp.Tools;

[McpServerToolType]
public sealed class KnownErrorTools(RepoPathService repo)
{
    [McpServerTool(Name = "get_known_errors")]
    [Description("Get AI known error patterns from the project registry. Optionally filter by area token such as docs, build, tests, runtime, config, CI, WPF, process, workflows, desktop-ui, or XAML.")]
    public string GetKnownErrors(
        [Description("Optional area filter token, e.g. docs | build | tests | runtime | config | CI | WPF | process")] string? area = null)
    {
        var content = ReadKnownErrors();
        if (content is null)
            return $"Known errors file not found at: {repo.KnownErrorsFile}";

        if (string.IsNullOrWhiteSpace(area))
            return content;

        var filtered = FilterByArea(content, area);
        return string.IsNullOrWhiteSpace(filtered)
            ? $"No known errors found for area '{area}'. Available areas include docs, build, tests, runtime, config, CI, WPF, process, workflows, desktop-ui, and XAML."
            : filtered;
    }

    [McpServerTool(Name = "search_known_errors")]
    [Description("Search the known error registry by keyword. Returns entries where the keyword appears in symptoms, root cause, prevention checklist, or verification commands.")]
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
        sb.AppendLine($"## Known Errors matching '{query}' ({matched.Count} found)");
        sb.AppendLine();
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
            .Where(e => EntryMatchesArea(e, area))
            .ToList();

        if (matched.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine($"## Known Errors - Area: {area} ({matched.Count} found)");
        sb.AppendLine();
        foreach (var entry in matched)
            sb.AppendLine(entry).AppendLine("---");
        return sb.ToString();
    }

    private static List<string> SplitIntoEntries(string content)
    {
        // Each entry starts with "### AI-".
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var entries = new List<string>();
        var current = new StringBuilder();

        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("### AI-", StringComparison.Ordinal) && current.Length > 0)
            {
                entries.Add(current.ToString().Trim());
                current.Clear();
            }

            current.AppendLine(line);
        }

        if (current.Length > 0)
            entries.Add(current.ToString().Trim());

        return entries
            .Where(e => e.StartsWith("### AI-", StringComparison.Ordinal))
            .ToList();
    }

    private static bool EntryMatchesArea(string entry, string area)
    {
        var requestedArea = area.Trim();
        if (requestedArea.Length == 0)
            return true;

        var entryArea = GetEntryMetadataValue(entry, "Area");
        if (string.IsNullOrWhiteSpace(entryArea))
            return false;

        var requestedTokens = SplitAreaTokens(requestedArea);
        if (requestedTokens.Count == 0)
            return false;

        var entryTokens = SplitAreaTokens(entryArea);
        return requestedTokens.All(entryTokens.Contains);
    }

    private static string? GetEntryMetadataValue(string entry, string key)
    {
        var prefix = $"- **{key}**:";
        var lines = entry.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return trimmed[prefix.Length..].Trim();
        }

        return null;
    }

    private static HashSet<string> SplitAreaTokens(string area)
    {
        return area
            .Split(['/', ',', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static token => token.ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
