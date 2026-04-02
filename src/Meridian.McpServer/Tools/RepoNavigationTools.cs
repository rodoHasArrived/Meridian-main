using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.McpServer.Navigation;

namespace Meridian.McpServer.Tools;

/// <summary>
/// MCP tools for routing work through the generated repository navigation dataset.
/// </summary>
[McpServerToolType]
[ImplementsAdr("ADR-004", "All async MCP-compatible tool surfaces remain cancellation-friendly even when synchronous")]
[ImplementsAdr("ADR-005", "Attribute-based MCP tool discovery")]
public sealed class RepoNavigationTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly RepoNavigationCatalog _catalog;
    private readonly ILogger<RepoNavigationTools> _log;

    public RepoNavigationTools(RepoNavigationCatalog catalog, ILogger<RepoNavigationTools> log)
    {
        _catalog = catalog;
        _log = log;
    }

    [McpServerTool, Description(
        "Find the most relevant Meridian subsystem for a task or topic. Returns summary, start projects, key contracts, entrypoints, and related docs.")]
    public string FindSubsystem(
        [Description("Subsystem name, project name, or topic such as 'providers', 'WPF', 'storage', or 'mcp'.")]
        string query)
    {
        _log.LogInformation("MCP tool {Tool} called — query={Query}", nameof(FindSubsystem), query);

        var subsystem = _catalog.FindSubsystem(query);
        if (subsystem is null)
            return JsonSerializer.Serialize(new { query, message = "No matching subsystem found." }, JsonOpts);

        return JsonSerializer.Serialize(subsystem, JsonOpts);
    }

    [McpServerTool, Description(
        "Route a task description to the best starting projects, docs, symbols, and specialist guidance. Use for orientation before deeper tracing.")]
    public string RouteTask(
        [Description("Natural-language task description such as 'add a provider', 'fix WPF issue', 'investigate storage bug', or 'where do I start for MCP work?'.")]
        string task)
    {
        _log.LogInformation("MCP tool {Tool} called — task={Task}", nameof(RouteTask), task);

        var routes = _catalog.FindRoutes(task);
        if (routes.Count == 0)
            return JsonSerializer.Serialize(new { task, message = "No task route matched the query." }, JsonOpts);

        return JsonSerializer.Serialize(new { task, routes }, JsonOpts);
    }

    [McpServerTool, Description(
        "Find high-signal entrypoints such as contracts, shell files, pipeline coordinators, and server registration files for a subsystem or topic.")]
    public string FindEntrypoints(
        [Description("Subsystem, project, or topic such as 'provider', 'storage', 'wpf', or 'mcp'.")]
        string query)
    {
        _log.LogInformation("MCP tool {Tool} called — query={Query}", nameof(FindEntrypoints), query);

        var subsystem = _catalog.FindSubsystem(query);
        var symbols = _catalog.FindSymbols(query);

        if (subsystem is null && symbols.Count == 0)
            return JsonSerializer.Serialize(new { query, message = "No entrypoints found." }, JsonOpts);

        return JsonSerializer.Serialize(new
        {
            query,
            subsystem = subsystem is null ? null : new
            {
                subsystem.Title,
                subsystem.Entrypoints,
                subsystem.KeyContracts
            },
            symbols = symbols.Select(symbol => new
            {
                symbol.Name,
                symbol.Path,
                symbol.Reason
            })
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Find closely related Meridian projects and dependency edges for a subsystem or project name. Useful when work crosses project boundaries.")]
    public string FindRelatedProjects(
        [Description("Subsystem or project name such as 'Meridian.Storage', 'providers', 'WPF', or 'mcp'.")]
        string query)
    {
        _log.LogInformation("MCP tool {Tool} called — query={Query}", nameof(FindRelatedProjects), query);
        return JsonSerializer.Serialize(_catalog.FindRelatedProjects(query), JsonOpts);
    }

    [McpServerTool, Description(
        "Find the most authoritative docs for a topic before implementation. Returns AI guides, plans, and developer docs that should be consulted first.")]
    public string FindAuthoritativeDocs(
        [Description("Topic such as 'provider', 'storage', 'WPF', 'fsharp', 'testing', or 'navigation'.")]
        string topic)
    {
        _log.LogInformation("MCP tool {Tool} called — topic={Topic}", nameof(FindAuthoritativeDocs), topic);

        var docs = _catalog.FindDocuments(topic);
        if (docs.Count == 0)
            return JsonSerializer.Serialize(new { topic, message = "No authoritative docs found." }, JsonOpts);

        return JsonSerializer.Serialize(new { topic, documents = docs }, JsonOpts);
    }
}
