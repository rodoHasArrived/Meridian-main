namespace Meridian.Mcp.Tools;

/// <summary>
/// Provides MCP tools to list, read, and inspect repository architecture decisions.
/// </summary>
[McpServerToolType]
public sealed class AdrTools(RepoPathService repo)
{
    [McpServerTool(Name = "list_adrs")]
    [Description("List all Architecture Decision Records, active and archived, with their IDs, titles, and status. Returns a Markdown table.")]
    public string ListAdrs(CancellationToken ct = default)
    {
        if (!Directory.Exists(repo.AdrPath) && !Directory.Exists(repo.ArchivedAdrPath))
            return $"ADR directory not found at: {repo.AdrPath}";

        var sb = new StringBuilder();
        sb.AppendLine("## Architecture Decision Records\n");
        sb.AppendLine("| ID | Title | Status |");
        sb.AppendLine("|----|-------|--------|");

        foreach (var (file, archived) in repo.EnumerateAdrFiles(ct))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var idPart = name.Split('-')[0];
            var title = ExtractTitle(file);
            var status = archived ? "🗄️ Archived" : ExtractStatus(file);
            sb.AppendLine($"| ADR-{idPart} | {title} | {status} |");
        }

        sb.AppendLine();
        sb.AppendLine("Archived records live in `archive/docs/adr/` and are kept for historical `ImplementsAdr` references.");
        sb.AppendLine("Use `get_adr` with an ID (e.g. `\"007\"`) to read the full content of any ADR.");
        return sb.ToString();
    }

    [McpServerTool(Name = "get_adr")]
    [Description("Get the full content of a specific Architecture Decision Record, active or archived. Accepts '001', 'ADR-001', or '1'.")]
    public string GetAdr(
        [Description("ADR ID, e.g. '007', 'ADR-007', or '7'")] string id,
        CancellationToken ct = default)
    {
        var normalized = NormalizeId(id);
        if (normalized is null)
            return $"Invalid ADR ID: '{id}'. Use a number like '007', 'ADR-007', or '7'.";

        if (!Directory.Exists(repo.AdrPath) && !Directory.Exists(repo.ArchivedAdrPath))
            return $"ADR directory not found at: {repo.AdrPath}";

        ct.ThrowIfCancellationRequested();
        var match = repo.FindAdrFile(normalized);
        if (match is null)
            return $"ADR '{normalized}' not found in {repo.AdrPath} or {repo.ArchivedAdrPath}. Use `list_adrs` to see available ADRs.";

        return File.ReadAllText(match);
    }

    private static string? NormalizeId(string id)
    {
        // Strip "ADR-" prefix
        var stripped = id.Trim().ToUpperInvariant().Replace("ADR-", "");
        if (!int.TryParse(stripped, out var num))
            return null;
        return num.ToString("D3");
    }

    private static string ExtractTitle(string filePath)
    {
        foreach (var line in File.ReadLines(filePath))
        {
            var trimmed = line.TrimStart('#', ' ');
            if (!string.IsNullOrWhiteSpace(trimmed))
                return trimmed;
        }
        return Path.GetFileNameWithoutExtension(filePath);
    }

    private static string ExtractStatus(string filePath)
    {
        foreach (var line in File.ReadLines(filePath))
        {
            var lower = line.ToLowerInvariant();
            if (lower.Contains("status:") || lower.Contains("**status**"))
            {
                if (lower.Contains("accepted"))
                    return "✅ Accepted";
                if (lower.Contains("rejected"))
                    return "❌ Rejected";
                if (lower.Contains("superseded"))
                    return "⚠️ Superseded";
                if (lower.Contains("deprecated"))
                    return "⚠️ Deprecated";
                if (lower.Contains("proposed"))
                    return "🔵 Proposed";
            }
        }
        return "✅ Accepted";
    }
}
