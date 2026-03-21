namespace Meridian.Mcp.Resources;

[McpServerResourceType]
public sealed class AdrResources(RepoPathService repo)
{
    [McpServerResource(UriTemplate = "mdc://adrs", Name = "adr_index",
        Title = "ADR Index — All Architecture Decision Records",
        MimeType = "text/markdown")]
    [Description("Index of all 17 Architecture Decision Records in the Meridian project.")]
    public string GetAdrIndex()
    {
        if (!Directory.Exists(repo.AdrPath))
            return $"ADR directory not found at {repo.AdrPath}";

        var sb = new StringBuilder();
        sb.AppendLine("# Meridian — Architecture Decision Records\n");
        sb.AppendLine("These ADRs are the architectural law of the project. Every significant implementation must reference the relevant ADR via `[ImplementsAdr(\"ADR-XXX\", \"reason\")]`.\n");
        sb.AppendLine("| ID | Title | Key Enforcement |");
        sb.AppendLine("|----|-------|-----------------|");

        var summaries = new Dictionary<string, string>
        {
            ["001"] = "Use `[ImplementsAdr(\"ADR-001\")]` on all providers",
            ["002"] = "Hot/warm/cold storage tiers; use `TierMigrationService`",
            ["003"] = "Monolith preferred — no microservice decomposition",
            ["004"] = "`CancellationToken ct = default` on every async method",
            ["005"] = "`[DataSource]` attribute for automatic discovery",
            ["006"] = "Domain events: sealed record + static factories",
            ["007"] = "`AtomicFileWriter` for all sink writes; WAL before queue",
            ["008"] = "`CompositeSink` for JSONL + Parquet simultaneous writes",
            ["009"] = "F# discriminated unions; handle all DU cases in C#",
            ["010"] = "`IHttpClientFactory` — never construct `HttpClient` directly",
            ["011"] = "Env vars for credentials; no hardcoding",
            ["012"] = "Unified health checks + Prometheus metrics pipeline",
            ["013"] = "`EventPipelinePolicy.*.CreateChannel<T>()` — no raw channel creation",
            ["014"] = "`MarketDataJsonContext` on all `JsonSerializer` calls",
        };

        foreach (var file in Directory.EnumerateFiles(repo.AdrPath, "*.md")
                     .Where(f => !Path.GetFileName(f).StartsWith("_") &&
                                 !Path.GetFileName(f).Equals("README.md", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(f => f))
        {
            var id = Path.GetFileNameWithoutExtension(file).Split('-')[0];
            var title = ExtractFirstHeading(file);
            summaries.TryGetValue(id, out var enforcement);
            sb.AppendLine($"| [ADR-{id}](mdc://adrs/{id}) | {title} | {enforcement ?? "—"} |");
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
        var normalized = id.TrimStart('0').PadLeft(3, '0');
        var match = Directory.EnumerateFiles(repo.AdrPath, $"{normalized}-*.md").FirstOrDefault();
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
