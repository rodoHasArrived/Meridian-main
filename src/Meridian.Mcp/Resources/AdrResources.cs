using Meridian.Core.Pipeline;
namespace Meridian.Mcp.Resources;

/// <summary>
/// Exposes the architecture decision index and individual decision records as MCP resources.
/// </summary>
[McpServerResourceType]
public sealed class AdrResources(RepoPathService repo)
{
    [McpServerResource(UriTemplate = "mdc://adrs", Name = "adr_index",
        Title = "ADR Index — Active and Archived Architecture Decision Records",
        MimeType = "text/markdown")]
    [Description("Index of the active and archived Architecture Decision Records in the Meridian project.")]
    public string GetAdrIndex()
    {
        if (!Directory.Exists(repo.AdrPath) && !Directory.Exists(repo.ArchivedAdrPath))
            return $"ADR directory not found at {repo.AdrPath}";

        var sb = new StringBuilder();
        sb.AppendLine("# Meridian — Architecture Decision Records\n");
        sb.AppendLine("These ADRs are the architectural law of the project. Every significant implementation must reference the relevant ADR via `[ImplementsAdr(\"ADR-XXX\", \"reason\")]`.\n");
        sb.AppendLine("| ID | Title | State | Key Enforcement |");
        sb.AppendLine("|----|-------|-------|-----------------|");

        var summaries = new Dictionary<string, string>
        {
            ["001"] = "Use `[ImplementsAdr(\"ADR-001\")]` on all providers",
            ["002"] = "Hot/warm/cold storage tiers; use `TierMigrationService`",
            ["004"] = "`CancellationToken ct = default` on every async method",
            ["005"] = "`[DataSource]` attribute for automatic discovery",
            ["006"] = "Domain events: sealed record + static factories",
            ["007"] = "`AtomicFileWriter` for all sink writes; WAL before queue",
            ["010"] = "`IHttpClientFactory` — never construct `HttpClient` directly",
            ["013"] = "`EventPipelinePolicy.*.CreateChannel<T>()` — no raw channel creation",
            ["014"] = "`MarketDataJsonContext` on all `JsonSerializer` calls",
        };

        foreach (var (file, archived) in repo.EnumerateAdrFiles())
        {
            var id = Path.GetFileNameWithoutExtension(file).Split('-')[0];
            var title = ExtractFirstHeading(file);
            summaries.TryGetValue(id, out var enforcement);
            var state = archived ? "Archived" : "Active";
            sb.AppendLine($"| [ADR-{id}](mdc://adrs/{id}) | {title} | {state} | {enforcement ?? "—"} |");
        }

        sb.AppendLine();
        sb.AppendLine("Access individual ADRs at `mdc://adrs/{id}` (e.g. `mdc://adrs/007`).");
        return sb.ToString();
    }

    [McpServerResource(UriTemplate = "mdc://adrs/{id}", Name = "adr",
        Title = "Architecture Decision Record",
        MimeType = "text/markdown")]
    [Description("Full content of a specific ADR. Use ID like '007' or '001'.")]
    public string GetAdr(string id)
    {
        // IDs are numbers, never filesystem patterns or paths. Keep wildcard characters and
        // separators out of EnumerateFiles even on hosts with permissive search patterns.
        if (string.IsNullOrEmpty(id) || id.Length > 3 || id.Any(c => c is < '0' or > '9'))
            return $"ADR '{id}' not found. Use a numeric ID such as '007'.";
        var normalized = id.PadLeft(3, '0');
        var match = repo.FindAdrFile(normalized);
        if (match is null)
            return $"ADR '{id}' not found. See mdc://adrs for the full index.";
        return File.ReadAllText(match);
    }

    private static string ExtractFirstHeading(string filePath)
    {
        foreach (var line in File.ReadLines(filePath))
        {
            var trimmed = line.TrimStart('#', ' ');
            if (!string.IsNullOrWhiteSpace(trimmed))
                return trimmed;
        }
        return Path.GetFileNameWithoutExtension(filePath);
    }
}
