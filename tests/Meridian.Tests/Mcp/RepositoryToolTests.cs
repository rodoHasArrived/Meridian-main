using System.Text.Json;
using FluentAssertions;
using Meridian.Mcp.Services;
using Meridian.Mcp.Resources;
using Meridian.Mcp.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Mcp;

/// <summary>Exercise tool contracts against an isolated repository, without real repo mutation.</summary>
public sealed class RepositoryToolTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"meridian-mcp-{Guid.NewGuid():N}");
    private readonly RepoPathService _repo;

    public RepositoryToolTests()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "Meridian.sln"), string.Empty);
        _repo = new RepoPathService(_root);
    }

    [Fact]
    public void AdrTools_ListAndRead_NormalizesIdsAndExcludesSupportDocuments()
    {
        Write("docs/adr/007-storage.md", "# Durable storage\n\n**Status**: Accepted\nEvidence.");
        Write("docs/adr/README.md", "# Decisions");
        Write("docs/adr/_template.md", "# Template");
        var tools = new AdrTools(_repo);

        tools.ListAdrs().Should().Contain("ADR-007").And.Contain("Durable storage")
            .And.NotContain("ADR-README").And.NotContain("ADR-_template");
        tools.GetAdr("ADR-7").Should().Contain("Evidence.");
        tools.GetAdr("007").Should().Be(tools.GetAdr("7"));
        tools.GetAdr("../*").Should().Contain("Invalid ADR ID");
        tools.GetAdr("999").Should().Contain("not found");
    }

    [Theory]
    [InlineData("*")]
    [InlineData("../007")]
    [InlineData("007/../../private")]
    [InlineData("007?")]
    [InlineData("-7")]
    [InlineData("1000")]
    public void AdrResource_RejectsPathsAndWildcardPatterns(string id)
    {
        Write("docs/adr/007-storage.md", "retained synthetic decision");
        var resource = new AdrResources(_repo);

        resource.GetAdr(id).Should().Contain("not found").And.NotContain("retained synthetic decision");
        resource.GetAdr("7").Should().Be("retained synthetic decision");
    }

    [Fact]
    public void ConventionTools_ReturnEmbeddedRulesAndOptionalRepositoryInstructions()
    {
        var tools = new ConventionTools(_repo);
        tools.GetCodingConventions().Should().Contain("CancellationToken").And.Contain("Structured logging");
        Write(".github/instructions/csharp.instructions.md", "A repository-specific convention.");

        tools.GetCodingConventions().Should().Contain("A repository-specific convention.");
        tools.GetAntiPatterns().Should().Contain("Swallowing exceptions").And.Contain("Correct approach");
    }

    [Fact]
    public void KnownErrorTools_FilterMetadataTokensRatherThanIncidentalBodyMentions()
    {
        Write("docs/ai/ai-known-errors.md", """
            # Registry
            ### AI-001 Build deadlock
            - **Area**: build / runtime
            - Prevention: use CancellationToken; docs mention only.
            ### AI-002 Broken documentation
            - **Area**: docs
            - Prevention: check links.
            """.Replace("\n", "\r\n", StringComparison.Ordinal));
        var tools = new KnownErrorTools(_repo);

        tools.GetKnownErrors("BUILD | runtime").Should().Contain("AI-001").And.NotContain("AI-002");
        tools.GetKnownErrors("docs").Should().Contain("AI-002").And.NotContain("AI-001");
        tools.GetKnownErrors("WPF").Should().Contain("No known errors");
        tools.SearchKnownErrors("cancellationtoken").Should().Contain("AI-001").And.NotContain("AI-002");
        tools.SearchKnownErrors(" ").Should().Contain("non-empty");
    }

    [Fact]
    public void ProviderTools_ListClassifiesProvidersAndDisplaysActualSealedClassName()
    {
        Write("src/Meridian.Infrastructure/Adapters/Demo/Streaming.cs", """
            [DataSource("demo-stream")]
            public sealed class DemoStreaming : IMarketDataClient { }
            """);
        Write("src/Meridian.Infrastructure/Adapters/Demo/Historical.cs", """
            [DataSource("demo-history")]
            public class DemoHistorical : BaseHistoricalDataProvider { }
            """);
        Write("src/Meridian.Infrastructure/Adapters/Demo/Search.cs", """
            [DataSource("demo-search")]
            public sealed class DemoSearch : ISymbolSearchProvider { }
            """);
        var tools = new ProviderTools(_repo);

        tools.ListProviders().Should().Contain("`DemoStreaming`").And.Contain("`DemoHistorical`")
            .And.Contain("`DemoSearch`").And.Contain("Historical Providers").And.Contain("Symbol Search Providers");
        Write("docs/examples/provider-template/TemplateSymbolSearchProvider.cs", "// synthetic provider template");
        tools.GetProviderTemplate("symbolsearch").Should().Be(tools.GetProviderTemplate("symbol-search"));
        tools.GetProviderTemplate("symbol-search").Should().Contain("synthetic provider template");
        tools.GetProviderTemplate("../../secrets").Should().Contain("Unknown provider type");
    }

    [Fact]
    public async Task AuditTools_MissingScriptReportsActionableFailure()
    {
        var tools = new AuditTools(_repo, NullLogger<AuditTools>.Instance);
        (await tools.RunCodeAuditAsync()).Should().Contain("Audit script not found");
    }

    [Fact]
    public async Task AuditTools_KeepFilterAsOneLiteralArgumentAndReportExitCode()
    {
        Write("build/scripts/ai-repo-updater.py", """
            import json, sys
            print(json.dumps(sys.argv[1:]))
            print("synthetic stderr", file=sys.stderr)
            sys.exit(3)
            """);
        var tools = new AuditTools(_repo, NullLogger<AuditTools>.Instance);
        const string filter = "src/quoted path/\"; --another-argument $(inert)";

        var result = await tools.RunCodeAuditAsync(filter);

        result.Should().Contain(JsonSerializer.Serialize(new[] { "audit-code", "--filter", filter },
            new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping })
            .Replace(",", ", ", StringComparison.Ordinal));
        result.Should().Contain("**Exit code:** 3").And.Contain("synthetic stderr");
        (await tools.RunProviderAuditAsync()).Should().Contain("audit-providers");
        (await tools.RunTestAuditAsync()).Should().Contain("audit-tests");
        (await tools.GetDiffSummaryAsync()).Should().Contain("diff-summary");
        (await tools.RunFullAuditAsync()).Should().Contain("Full Repository Audit");
    }

    private void Write(string relativePath, string contents)
    {
        var path = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
