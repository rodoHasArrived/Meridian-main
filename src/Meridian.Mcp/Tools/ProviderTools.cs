namespace Meridian.Mcp.Tools;

[McpServerToolType]
public sealed class ProviderTools(RepoPathService repo)
{
    private static readonly Dictionary<string, string> TemplateFileMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["streaming"] = "TemplateMarketDataClient.cs",
        ["historical"] = "TemplateHistoricalDataProvider.cs",
        ["symbol-search"] = "TemplateSymbolSearchProvider.cs",
        ["symbolsearch"] = "TemplateSymbolSearchProvider.cs",
        ["factory"] = "TemplateFactory.cs",
        ["config"] = "TemplateConfig.cs",
        ["constants"] = "TemplateConstants.cs",
    };

    [McpServerTool(Name = "get_provider_template")]
    [Description("Get a scaffold template file for implementing a new Meridian provider. Type: streaming | historical | symbol-search | factory | config | constants")]
    public string GetProviderTemplate(
        [Description("Provider type: streaming | historical | symbol-search | factory | config | constants")] string type)
    {
        if (!TemplateFileMap.TryGetValue(type, out var fileName))
        {
            return $"Unknown provider type '{type}'.\n\n" +
                   $"Available types: {string.Join(", ", TemplateFileMap.Keys.Where(k => !k.Contains("symbol") || k == "symbol-search").Distinct())}";
        }

        var path = Path.Combine(repo.TemplatesPath, fileName);
        if (!File.Exists(path))
            return $"Template file not found: {path}\n\nEnsure the repository is at {repo.Root}";

        var sb = new StringBuilder();
        sb.AppendLine($"## Template: {fileName}\n");
        sb.AppendLine($"**Path in repo:** `docs/examples/provider-template/{fileName}`\n");
        sb.AppendLine("Copy this file to `src/Meridian.Infrastructure/Adapters/{YourProvider}/` and replace all `Template` references with your provider name.\n");
        sb.AppendLine("```csharp");
        sb.AppendLine(File.ReadAllText(path));
        sb.AppendLine("```");
        return sb.ToString();
    }

    [McpServerTool(Name = "list_providers")]
    [Description("List all registered data providers in the Meridian codebase by scanning [DataSource] attributes. Returns provider name, type, class, and location.")]
    public string ListProviders()
    {
        if (!Directory.Exists(repo.AdaptersPath))
            return $"Adapters directory not found at: {repo.AdaptersPath}";

        var sb = new StringBuilder();
        sb.AppendLine("## Registered Data Providers\n");

        var streaming = new List<(string name, string cls, string path)>();
        var historical = new List<(string name, string cls, string path)>();
        var symbolSearch = new List<(string name, string cls, string path)>();

        foreach (var file in Directory.EnumerateFiles(repo.AdaptersPath, "*.cs", SearchOption.AllDirectories))
        {
            var content = File.ReadAllText(file);
            if (!content.Contains("[DataSource("))
                continue;

            var relativePath = Path.GetRelativePath(repo.Root, file).Replace('\\', '/');
            var className = ExtractClassName(content);
            var dataSourceId = ExtractDataSourceId(content);

            if (content.Contains("IMarketDataClient") || content.Contains("IRealtimeDataSource"))
                streaming.Add((dataSourceId, className, relativePath));
            else if (content.Contains("IHistoricalDataProvider") || content.Contains("BaseHistoricalDataProvider"))
                historical.Add((dataSourceId, className, relativePath));
            else if (content.Contains("ISymbolSearchProvider") || content.Contains("BaseSymbolSearchProvider"))
                symbolSearch.Add((dataSourceId, className, relativePath));
            else
                streaming.Add((dataSourceId, className, relativePath)); // default bucket
        }

        AppendProviderSection(sb, "Streaming Providers (IMarketDataClient)", streaming);
        AppendProviderSection(sb, "Historical Providers (IHistoricalDataProvider)", historical);
        AppendProviderSection(sb, "Symbol Search Providers (ISymbolSearchProvider)", symbolSearch);

        sb.AppendLine("\nUse `get_provider_template` to get a scaffold for a new provider.");
        return sb.ToString();
    }

    private static void AppendProviderSection(StringBuilder sb, string title, List<(string name, string cls, string path)> providers)
    {
        if (providers.Count == 0)
            return;
        sb.AppendLine($"### {title}\n");
        sb.AppendLine("| Provider ID | Class | File |");
        sb.AppendLine("|-------------|-------|------|");
        foreach (var (name, cls, path) in providers.OrderBy(p => p.name))
            sb.AppendLine($"| `{name}` | `{cls}` | `{path}` |");
        sb.AppendLine();
    }

    private static string ExtractClassName(string content)
    {
        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("public sealed class ") || trimmed.StartsWith("public class "))
            {
                var parts = trimmed.Split(' ');
                return parts.Length >= 3 ? parts[2].Split('(')[0].Split(':')[0].Trim() : "Unknown";
            }
        }
        return "Unknown";
    }

    private static string ExtractDataSourceId(string content)
    {
        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("[DataSource("))
            {
                var start = trimmed.IndexOf('"');
                var end = trimmed.IndexOf('"', start + 1);
                if (start >= 0 && end > start)
                    return trimmed.Substring(start + 1, end - start - 1);
            }
        }
        return "unknown";
    }
}
