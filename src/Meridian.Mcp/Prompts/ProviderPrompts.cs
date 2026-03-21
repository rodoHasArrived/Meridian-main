namespace Meridian.Mcp.Prompts;

[McpServerPromptType]
public sealed class ProviderPrompts(RepoPathService repo)
{
    [McpServerPrompt(Name = "implement_provider")]
    [Description("Full context prompt for implementing a new Meridian provider. Includes the template, required ADRs, base classes, and implementation checklist.")]
    public string ImplementProvider(
        [Description("Provider display name, e.g. 'Kraken' or 'Binance'")] string providerName,
        [Description("Provider type: streaming | historical | symbol-search")] string providerType,
        [Description("Brief description of what data this provider supplies")] string description)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Implement a new {providerType} provider: {providerName}");
        sb.AppendLine();
        sb.AppendLine($"**Description:** {description}");
        sb.AppendLine();

        sb.AppendLine("## Required ADR Compliance");
        sb.AppendLine();
        sb.AppendLine("Every provider in Meridian MUST comply with these ADRs:");
        sb.AppendLine();
        sb.AppendLine("- **ADR-001** — Implement the correct interface (`IMarketDataClient`, `IHistoricalDataProvider`, or `ISymbolSearchProvider`)");
        sb.AppendLine("- **ADR-004** — Every `async` method must accept `CancellationToken ct = default`");
        sb.AppendLine("- **ADR-005** — Decorate the class with `[DataSource(\"id\", \"Display Name\", ...)]` for auto-discovery");
        sb.AppendLine("- **ADR-010** — Never construct `HttpClient` directly — inject `IHttpClientFactory`");
        sb.AppendLine("- **ADR-011** — Read credentials from environment variables only; never hardcode");
        sb.AppendLine();

        sb.AppendLine("## Required Attributes");
        sb.AppendLine();
        sb.AppendLine("```csharp");
        var idSlug = providerName.ToLowerInvariant().Replace(" ", "-");
        sb.AppendLine($"[DataSource(\"{idSlug}\", \"{providerName}\", DataSourceType.{MapType(providerType)}, DataSourceCategory.Exchange)]");
        sb.AppendLine("[ImplementsAdr(\"ADR-001\", \"Provider abstraction pattern\")]");
        sb.AppendLine("[ImplementsAdr(\"ADR-004\", \"CancellationToken on all async methods\")]");
        sb.AppendLine("[ImplementsAdr(\"ADR-005\", \"Attribute-based discovery\")]");
        sb.AppendLine($"public sealed class {providerName.Replace(" ", "")}MarketDataClient : BaseMarketDataClient");
        sb.AppendLine("```");
        sb.AppendLine();

        // Append template if available
        var templateFile = MapTemplateFile(providerType);
        var templatePath = Path.Combine(repo.TemplatesPath, templateFile);
        if (File.Exists(templatePath))
        {
            sb.AppendLine($"## Template: `{templateFile}`");
            sb.AppendLine();
            sb.AppendLine($"Copy this from `docs/examples/provider-template/{templateFile}`");
            sb.AppendLine("and replace all `Template` references with your provider name.");
            sb.AppendLine();
            sb.AppendLine("```csharp");
            sb.AppendLine(File.ReadAllText(templatePath));
            sb.AppendLine("```");
            sb.AppendLine();
        }

        // Append provider guide if available
        if (File.Exists(repo.ProvidersGuideFile))
        {
            sb.AppendLine("## Provider Implementation Guide");
            sb.AppendLine();
            sb.Append(File.ReadAllText(repo.ProvidersGuideFile));
            sb.AppendLine();
        }

        sb.AppendLine("## Implementation Checklist");
        sb.AppendLine();
        sb.AppendLine("- [ ] Class is `sealed`");
        sb.AppendLine("- [ ] `[DataSource]` attribute with correct id, display name, type, and category");
        sb.AppendLine("- [ ] `[ImplementsAdr(\"ADR-001\")]`, `[ImplementsAdr(\"ADR-004\")]`, `[ImplementsAdr(\"ADR-005\")]`");
        sb.AppendLine("- [ ] Every async method has `CancellationToken ct = default`");
        sb.AppendLine("- [ ] Credentials read from environment variables via `ISecretProvider`");
        sb.AppendLine("- [ ] HTTP via `IHttpClientFactory` — no direct `HttpClient` construction");
        sb.AppendLine("- [ ] Structured logging: `_logger.LogInformation(\"{Symbol}: {Count} bars\", symbol, count)`");
        sb.AppendLine("- [ ] Rate limiting via `WaitForRateLimitSlotAsync(ct)` (historical providers)");
        sb.AppendLine("- [ ] Test file created in `tests/Meridian.Tests/Infrastructure/Providers/`");
        sb.AppendLine("- [ ] Registered in `ProviderFactory` or `ServiceCompositionRoot`");
        sb.AppendLine();
        sb.AppendLine("Use `get_adr` to read the full content of any ADR, and `run_provider_audit` after implementation to verify compliance.");

        return sb.ToString();
    }

    private static string MapType(string type) => type.ToLowerInvariant() switch
    {
        "streaming" => "Realtime",
        "historical" => "Historical",
        "symbol-search" => "Historical",
        _ => "Hybrid",
    };

    private static string MapTemplateFile(string type) => type.ToLowerInvariant() switch
    {
        "streaming" => "TemplateMarketDataClient.cs",
        "historical" => "TemplateHistoricalDataProvider.cs",
        "symbol-search" => "TemplateSymbolSearchProvider.cs",
        _ => "TemplateMarketDataClient.cs",
    };
}
