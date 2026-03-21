namespace Meridian.Mcp.Resources;

[McpServerResourceType]
public sealed class TemplateResources(RepoPathService repo)
{
    [McpServerResource(UriTemplate = "mdc://templates/{type}", Name = "provider_template",
        Title = "Provider Implementation Template",
        MimeType = "text/plain")]
    [Description("Scaffold template for a new Meridian provider. type: streaming | historical | symbol-search | factory | config | constants")]
    public string GetTemplate(string type)
    {
        var fileMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["streaming"] = "TemplateMarketDataClient.cs",
            ["historical"] = "TemplateHistoricalDataProvider.cs",
            ["symbol-search"] = "TemplateSymbolSearchProvider.cs",
            ["factory"] = "TemplateFactory.cs",
            ["config"] = "TemplateConfig.cs",
            ["constants"] = "TemplateConstants.cs",
        };

        if (!fileMap.TryGetValue(type, out var fileName))
            return $"Unknown template type '{type}'. Available: {string.Join(", ", fileMap.Keys)}";

        var path = Path.Combine(repo.TemplatesPath, fileName);
        return File.Exists(path) ? File.ReadAllText(path) : $"Template file not found: {path}";
    }
}
