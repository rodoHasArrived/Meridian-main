using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.McpServer.Navigation;

namespace Meridian.McpServer.Resources;

/// <summary>
/// MCP resources that expose generated repository navigation truth for assistants.
/// </summary>
[McpServerResourceType]
[ImplementsAdr("ADR-005", "Attribute-based MCP resource discovery")]
public sealed class RepoNavigationResources
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly RepoNavigationCatalog _catalog;

    public RepoNavigationResources(RepoNavigationCatalog catalog)
    {
        _catalog = catalog;
    }

    [McpServerResource(
        UriTemplate = "mdc://repo-navigation/catalog",
        Name = "Repo Navigation Catalog",
        MimeType = "application/json",
        Title = "Generated high-signal repository navigation dataset with subsystems, routes, docs, symbols, and dependencies.")]
    public string GetRepoNavigationCatalog() => _catalog.GetCatalogJson();

    [McpServerResource(
        UriTemplate = "mdc://repo-navigation/quick-start",
        Name = "Repo Navigation Quick Start",
        MimeType = "application/json",
        Title = "Condensed orientation view for routing work into the right Meridian subsystem before detailed exploration.")]
    public string GetRepoNavigationQuickStart()
    {
        return JsonSerializer.Serialize(_catalog.GetQuickStart(), JsonOpts);
    }
}

