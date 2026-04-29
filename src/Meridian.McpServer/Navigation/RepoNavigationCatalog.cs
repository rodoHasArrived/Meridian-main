using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.McpServer.Navigation;

/// <summary>
/// Loads and queries the generated AI navigation dataset.
/// </summary>
[ImplementsAdr("ADR-005", "Attribute-based MCP resource and tool discovery uses generated navigation data")]
public sealed class RepoNavigationCatalog
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly ILogger<RepoNavigationCatalog> _log;
    private readonly string _dataPath;
    private RepoNavigationData? _cached;

    public RepoNavigationCatalog(IHostEnvironment environment, ILogger<RepoNavigationCatalog> log)
        : this(ResolveDataPath(environment.ContentRootPath), log)
    {
    }

    public RepoNavigationCatalog(string dataPath, ILogger<RepoNavigationCatalog>? log = null)
    {
        _dataPath = dataPath;
        _log = log ?? NullLogger<RepoNavigationCatalog>.Instance;
    }

    public RepoNavigationData Load()
    {
        if (_cached is not null)
            return _cached;

        if (!File.Exists(_dataPath))
            throw new FileNotFoundException($"Repo navigation dataset not found at '{_dataPath}'.", _dataPath);

        _log.LogInformation("Loading repo navigation dataset from {Path}", _dataPath);
        var json = File.ReadAllText(_dataPath);
        _cached = JsonSerializer.Deserialize<RepoNavigationData>(json, JsonOpts)
            ?? throw new InvalidOperationException($"Failed to deserialize repo navigation dataset at '{_dataPath}'.");
        return _cached;
    }

    public string GetDataPath() => _dataPath;

    public string GetCatalogJson()
    {
        var data = Load();
        return JsonSerializer.Serialize(data, JsonOpts);
    }

    public object GetQuickStart()
    {
        var data = Load();
        return new
        {
            data.GeneratedAt,
            subsystemCount = data.Subsystems.Count,
            routeCount = data.TaskRoutes.Count,
            subsystems = data.Subsystems.Select(s => new
            {
                s.Id,
                s.Title,
                s.RecommendedStart,
                projectCount = s.Projects.Count
            }),
            routes = data.TaskRoutes.Select(r => new
            {
                r.Id,
                r.Title,
                r.StartProjects,
                r.RecommendedSkill,
                r.RecommendedAgent
            })
        };
    }

    public RepoNavigationSubsystem? FindSubsystem(string query)
    {
        var normalized = Normalize(query);
        return Load().Subsystems
            .Select(subsystem => new
            {
                subsystem,
                score = Score(normalized, subsystem.Title, subsystem.Id, subsystem.Projects, subsystem.Keywords)
            })
            .Where(item => item.score > 0)
            .OrderByDescending(item => item.score)
            .ThenBy(item => item.subsystem.Title)
            .Select(item => item.subsystem)
            .FirstOrDefault();
    }

    public IReadOnlyList<RepoNavigationDocument> FindDocuments(string query, int limit = 5)
    {
        var normalized = Normalize(query);
        return Load().Documents
            .Select(document => new
            {
                document,
                score = Score(normalized, document.Title, document.Area, new[] { document.Path }, document.Keywords)
            })
            .Where(item => item.score > 0)
            .OrderByDescending(item => item.score)
            .ThenBy(item => item.document.Path)
            .Take(limit)
            .Select(item => item.document)
            .ToArray();
    }

    public IReadOnlyList<RepoNavigationSymbol> FindSymbols(string query, int limit = 6)
    {
        var normalized = Normalize(query);
        return Load().Symbols
            .Select(symbol => new
            {
                symbol,
                score = Score(normalized, symbol.Name, symbol.Project, new[] { symbol.Path, symbol.Kind }, symbol.Keywords)
            })
            .Where(item => item.score > 0)
            .OrderByDescending(item => item.score)
            .ThenBy(item => item.symbol.Name)
            .Take(limit)
            .Select(item => item.symbol)
            .ToArray();
    }

    public IReadOnlyList<RepoNavigationRoute> FindRoutes(string query, int limit = 3)
    {
        var normalized = Normalize(query);
        return Load().TaskRoutes
            .Select(route => new
            {
                route,
                score = Score(normalized, route.Title, route.Description, route.StartProjects, route.Keywords)
            })
            .Where(item => item.score > 0)
            .OrderByDescending(item => item.score)
            .ThenBy(item => item.route.Title)
            .Take(limit)
            .Select(item => item.route)
            .ToArray();
    }

    public object FindRelatedProjects(string query)
    {
        var data = Load();
        var normalizedQuery = Normalize(query);
        var project = data.Projects.FirstOrDefault(item =>
            Normalize(item.Name).Equals(normalizedQuery, StringComparison.Ordinal));

        if (project is not null)
            return BuildProjectRelationshipResult(query, project, data.Dependencies);

        var subsystem = FindSubsystem(query);
        if (subsystem is not null)
        {
            var dependencies = data.Dependencies
                .Where(edge => subsystem.Projects.Contains(edge.From, StringComparer.OrdinalIgnoreCase)
                    || subsystem.Projects.Contains(edge.To, StringComparer.OrdinalIgnoreCase))
                .ToArray();

            return new
            {
                query,
                matchedSubsystem = subsystem.Title,
                projects = subsystem.Projects,
                dependencyEdges = dependencies
            };
        }

        project = data.Projects.FirstOrDefault(item =>
            Normalize(item.Name).Contains(normalizedQuery, StringComparison.Ordinal));

        if (project is null)
        {
            return new
            {
                query,
                message = "No related projects found."
            };
        }

        return BuildProjectRelationshipResult(query, project, data.Dependencies);
    }

    private static object BuildProjectRelationshipResult(
        string query,
        RepoNavigationProject project,
        IEnumerable<RepoNavigationDependency> dependencies)
    {
        var edges = dependencies
            .Where(edge => edge.From.Equals(project.Name, StringComparison.OrdinalIgnoreCase)
                || edge.To.Equals(project.Name, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return new
        {
            query,
            project = project.Name,
            project.Path,
            references = project.ProjectReferences,
            dependencyEdges = edges
        };
    }

    public static string ResolveDataPath(string contentRootPath)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "navigation-data", "repo-navigation.json"),
            Path.Combine(contentRootPath, "..", "..", "docs", "ai", "generated", "repo-navigation.json"),
            Path.Combine(contentRootPath, "docs", "ai", "generated", "repo-navigation.json"),
        };

        foreach (var candidate in candidates.Select(Path.GetFullPath))
        {
            if (File.Exists(candidate))
                return candidate;
        }

        var current = new DirectoryInfo(Path.GetFullPath(contentRootPath));
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "docs", "ai", "generated", "repo-navigation.json");
            if (File.Exists(candidate))
                return candidate;
            current = current.Parent;
        }

        return Path.GetFullPath(candidates[0]);
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();

    private static int Score(string query, string primary, string secondary, IEnumerable<string> searchable, IEnumerable<string> keywords)
    {
        var score = 0;
        var queryTerms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var fields = new List<string>
        {
            Normalize(primary),
            Normalize(secondary),
        };
        fields.AddRange(searchable.Select(Normalize));
        fields.AddRange(keywords.Select(Normalize));

        foreach (var term in queryTerms)
        {
            if (fields.Any(field => field.Equals(term, StringComparison.Ordinal)))
            {
                score += 5;
                continue;
            }

            if (fields.Any(field => field.Contains(term, StringComparison.Ordinal)))
                score += 2;
        }

        return score;
    }
}

public sealed record RepoNavigationData(
    string GeneratedAt,
    string GeneratorVersion,
    string RepositoryRoot,
    IReadOnlyList<RepoNavigationSubsystem> Subsystems,
    IReadOnlyList<RepoNavigationProject> Projects,
    IReadOnlyList<RepoNavigationDocument> Documents,
    IReadOnlyList<RepoNavigationSymbol> Symbols,
    IReadOnlyList<RepoNavigationRoute> TaskRoutes,
    IReadOnlyList<RepoNavigationDependency> Dependencies);

public sealed record RepoNavigationSubsystem(
    string Id,
    string Title,
    string Summary,
    IReadOnlyList<string> Projects,
    IReadOnlyList<string> ProjectPaths,
    IReadOnlyList<string> CommonTasks,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<string> KeyContracts,
    IReadOnlyList<string> Entrypoints,
    IReadOnlyList<string> RelatedDocs,
    string RecommendedStart,
    string DocCoverageHint);

public sealed record RepoNavigationProject(
    string Name,
    string Path,
    string Kind,
    string Subsystem,
    string Summary,
    IReadOnlyList<string> CommonTasks,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<string> Entrypoints,
    IReadOnlyList<string> KeyContracts,
    IReadOnlyList<string> ProjectReferences,
    IReadOnlyList<string> RelatedDocs);

public sealed record RepoNavigationDocument(
    string Path,
    string Title,
    string Area,
    string WhenToConsult,
    IReadOnlyList<string> Keywords);

public sealed record RepoNavigationSymbol(
    string Name,
    string Kind,
    string Path,
    string Project,
    string Reason,
    IReadOnlyList<string> Keywords);

public sealed record RepoNavigationRoute(
    string Id,
    string Title,
    string Description,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<string> Subsystems,
    IReadOnlyList<string> SubsystemTitles,
    IReadOnlyList<string> StartProjects,
    IReadOnlyList<RepoNavigationRouteSymbol> StartSymbols,
    IReadOnlyList<string> AuthoritativeDocs,
    string RecommendedSkill,
    string RecommendedAgent);

public sealed record RepoNavigationRouteSymbol(
    string Name,
    string Path,
    string Reason);

public sealed record RepoNavigationDependency(
    string From,
    string To,
    string Reason);
