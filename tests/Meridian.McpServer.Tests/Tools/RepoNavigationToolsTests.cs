using Meridian.McpServer.Navigation;

namespace Meridian.McpServer.Tests.Tools;

public sealed class RepoNavigationToolsTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    private readonly string _dataPath;

    public RepoNavigationToolsTests()
    {
        Directory.CreateDirectory(_tempDir);
        _dataPath = Path.Combine(_tempDir, "repo-navigation.json");
        File.WriteAllText(_dataPath, """
        {
          "generatedAt": "2026-03-31T00:00:00Z",
          "generatorVersion": "1.0",
          "repositoryRoot": "C:/repo",
          "subsystems": [
            {
              "id": "providers-data",
              "title": "Providers and Storage",
              "summary": "Provider and storage subsystem.",
              "projects": ["Meridian.ProviderSdk", "Meridian.Infrastructure", "Meridian.Storage"],
              "projectPaths": ["src/Meridian.ProviderSdk", "src/Meridian.Infrastructure", "src/Meridian.Storage"],
              "commonTasks": ["add provider", "storage bug"],
              "keywords": ["provider", "storage", "backfill", "wal"],
              "keyContracts": [
                "src/Meridian.ProviderSdk/IMarketDataClient.cs",
                "src/Meridian.Infrastructure/Adapters/Core/IHistoricalDataProvider.cs"
              ],
              "entrypoints": [
                "src/Meridian.Infrastructure/Adapters",
                "src/Meridian.Storage/Archival/WriteAheadLog.cs"
              ],
              "relatedDocs": ["docs/ai/claude/CLAUDE.providers.md", "docs/ai/claude/CLAUDE.storage.md"],
              "recommendedStart": "Meridian.ProviderSdk",
              "docCoverageHint": "high"
            },
            {
              "id": "mcp-integration",
              "title": "MCP Integration",
              "summary": "MCP subsystem.",
              "projects": ["Meridian.McpServer", "Meridian.Mcp"],
              "projectPaths": ["src/Meridian.McpServer", "src/Meridian.Mcp"],
              "commonTasks": ["mcp work"],
              "keywords": ["mcp", "tool", "resource", "prompt"],
              "keyContracts": ["src/Meridian.McpServer/Program.cs"],
              "entrypoints": ["src/Meridian.McpServer/Program.cs"],
              "relatedDocs": ["docs/ai/navigation/README.md"],
              "recommendedStart": "Meridian.McpServer",
              "docCoverageHint": "high"
            }
          ],
          "projects": [
            {
              "name": "Meridian.ProviderSdk",
              "path": "src/Meridian.ProviderSdk",
              "kind": "sdk",
              "subsystem": "providers-data",
              "summary": "Provider contracts.",
              "commonTasks": ["add provider"],
              "keywords": ["provider", "streaming"],
              "entrypoints": ["src/Meridian.ProviderSdk/IMarketDataClient.cs"],
              "keyContracts": ["src/Meridian.ProviderSdk/IMarketDataClient.cs"],
              "projectReferences": [],
              "relatedDocs": ["docs/ai/claude/CLAUDE.providers.md"]
            },
            {
              "name": "Meridian.Storage",
              "path": "src/Meridian.Storage",
              "kind": "storage",
              "subsystem": "providers-data",
              "summary": "Storage.",
              "commonTasks": ["storage bug"],
              "keywords": ["storage", "wal"],
              "entrypoints": ["src/Meridian.Storage/Archival/WriteAheadLog.cs"],
              "keyContracts": ["src/Meridian.Storage/Archival/WriteAheadLog.cs"],
              "projectReferences": ["Meridian.Contracts"],
              "relatedDocs": ["docs/ai/claude/CLAUDE.storage.md"]
            },
            {
              "name": "Meridian.McpServer",
              "path": "src/Meridian.McpServer",
              "kind": "mcp-server",
              "subsystem": "mcp-integration",
              "summary": "MCP server.",
              "commonTasks": ["mcp work"],
              "keywords": ["mcp", "server"],
              "entrypoints": ["src/Meridian.McpServer/Program.cs"],
              "keyContracts": ["src/Meridian.McpServer/Program.cs"],
              "projectReferences": ["Meridian.Application"],
              "relatedDocs": ["docs/ai/navigation/README.md"]
            }
          ],
          "documents": [
            {
              "path": "docs/ai/claude/CLAUDE.providers.md",
              "title": "Provider guide",
              "area": "providers",
              "whenToConsult": "Provider work",
              "keywords": ["provider", "adapter", "backfill"]
            },
            {
              "path": "docs/ai/navigation/README.md",
              "title": "Navigation guide",
              "area": "ai-navigation",
              "whenToConsult": "Orientation",
              "keywords": ["navigation", "mcp", "routing"]
            }
          ],
          "symbols": [
            {
              "name": "IMarketDataClient",
              "kind": "interface",
              "path": "src/Meridian.ProviderSdk/IMarketDataClient.cs",
              "project": "Meridian.ProviderSdk",
              "reason": "Provider entrypoint.",
              "keywords": ["provider", "streaming"]
            },
            {
              "name": "Program",
              "kind": "mcp-entrypoint",
              "path": "src/Meridian.McpServer/Program.cs",
              "project": "Meridian.McpServer",
              "reason": "MCP entrypoint.",
              "keywords": ["mcp", "server", "registration"]
            }
          ],
          "taskRoutes": [
            {
              "id": "provider-work",
              "title": "Provider implementation and provider bugs",
              "description": "Provider route.",
              "keywords": ["provider", "adapter", "backfill"],
              "subsystems": ["providers-data"],
              "subsystemTitles": ["Providers and Storage"],
              "startProjects": ["Meridian.ProviderSdk", "Meridian.Infrastructure", "Meridian.Storage"],
              "startSymbols": [
                {
                  "name": "IMarketDataClient",
                  "path": "src/Meridian.ProviderSdk/IMarketDataClient.cs",
                  "reason": "Provider entrypoint."
                }
              ],
              "authoritativeDocs": ["docs/ai/claude/CLAUDE.providers.md"],
              "recommendedSkill": "meridian-provider-builder",
              "recommendedAgent": "provider-builder-agent"
            },
            {
              "id": "mcp-surface",
              "title": "MCP tools, prompts, and resources",
              "description": "MCP route.",
              "keywords": ["mcp", "tool", "resource", "prompt", "server"],
              "subsystems": ["mcp-integration"],
              "subsystemTitles": ["MCP Integration"],
              "startProjects": ["Meridian.McpServer", "Meridian.Mcp"],
              "startSymbols": [
                {
                  "name": "Program",
                  "path": "src/Meridian.McpServer/Program.cs",
                  "reason": "MCP entrypoint."
                }
              ],
              "authoritativeDocs": ["docs/ai/navigation/README.md"],
              "recommendedSkill": "meridian-repo-navigation",
              "recommendedAgent": "repo-navigation-agent"
            }
          ],
          "dependencies": [
            {
              "from": "Meridian.McpServer",
              "to": "Meridian.Application",
              "reason": "MCP server composes application services."
            },
            {
              "from": "Meridian.Storage",
              "to": "Meridian.Contracts",
              "reason": "Storage uses shared contracts."
            }
          ]
        }
        """);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private RepoNavigationTools CreateSut()
    {
        var catalog = new RepoNavigationCatalog(_dataPath);
        return new RepoNavigationTools(catalog, NullLogger<RepoNavigationTools>.Instance);
    }

    [Fact]
    public void FindSubsystem_ProviderQuery_ReturnsProviderSubsystem()
    {
        var sut = CreateSut();
        var json = sut.FindSubsystem("provider");

        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("title").GetString().Should().Be("Providers and Storage");
        doc.RootElement.GetProperty("recommendedStart").GetString().Should().Be("Meridian.ProviderSdk");
    }

    [Fact]
    public void RouteTask_McpQuery_ReturnsMcpRoute()
    {
        var sut = CreateSut();
        var json = sut.RouteTask("where do I start for mcp work?");

        var doc = JsonDocument.Parse(json);
        var routes = doc.RootElement.GetProperty("routes").EnumerateArray().ToArray();
        routes.Should().NotBeEmpty();
        routes[0].GetProperty("id").GetString().Should().Be("mcp-surface");
    }

    [Fact]
    public void FindAuthoritativeDocs_ProviderTopic_ReturnsProviderGuide()
    {
        var sut = CreateSut();
        var json = sut.FindAuthoritativeDocs("provider");

        var doc = JsonDocument.Parse(json);
        var documents = doc.RootElement.GetProperty("documents").EnumerateArray().ToArray();
        documents.Should().Contain(item => item.GetProperty("path").GetString() == "docs/ai/claude/CLAUDE.providers.md");
    }

    [Fact]
    public void FindRelatedProjects_ProjectName_ReturnsDependencyEdges()
    {
        var sut = CreateSut();
        var json = sut.FindRelatedProjects("Meridian.McpServer");

        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("project").GetString().Should().Be("Meridian.McpServer");
        doc.RootElement.GetProperty("dependencyEdges").GetArrayLength().Should().BeGreaterThan(0);
    }
}
