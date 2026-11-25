using System.Reflection;
using FluentAssertions;
using Meridian.Mcp.Tools;
using ModelContextProtocol.Server;
using Xunit;

namespace Meridian.Tests.Mcp;

/// <summary>
/// Every ledger MCP tool must be routable: named in the MCP README routing table, the Claude
/// project context, and the accounting skill, so assistants know when to call it instead of
/// reading journal files.
/// </summary>
public sealed class LedgerToolRoutingTests
{
    private static readonly string[] RoutingDocuments =
    [
        "src/Meridian.Mcp/README.md",
        ".claude/skills/_shared/project-context.md",
        ".claude/skills/meridian-accounting/SKILL.md",
    ];

    [Fact]
    public void LedgerTools_ExposeTheDocumentedToolSet()
    {
        LedgerToolNames().Should().HaveCount(11).And.OnlyHaveUniqueItems();
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public void EveryLedgerTool_IsNamedInRoutingDocument(string relativePath)
    {
        var text = File.ReadAllText(Path.Combine(FindRealRepoRoot(), relativePath));

        var missing = LedgerToolNames().Where(name => !text.Contains($"`{name}`", StringComparison.Ordinal));

        missing.Should().BeEmpty("{0} must route every ledger MCP tool", relativePath);
    }

    [Fact]
    public void ReadmeRoutingTable_HasOneRowPerLedgerTool()
    {
        var readme = File.ReadAllText(Path.Combine(FindRealRepoRoot(), "src/Meridian.Mcp/README.md"));
        var section = readme[readme.IndexOf("#### Ledger tool routing", StringComparison.Ordinal)..];

        foreach (var name in LedgerToolNames())
        {
            section.Split('\n').Count(line => line.StartsWith('|') && line.Contains($"| `{name}` |", StringComparison.Ordinal))
                .Should().Be(1, "the routing table should have exactly one row for {0}", name);
        }
    }

    public static TheoryData<string> Documents()
    {
        var data = new TheoryData<string>();
        foreach (var path in RoutingDocuments)
            data.Add(path);
        return data;
    }

    private static string[] LedgerToolNames() =>
        typeof(LedgerTools).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(method => method.GetCustomAttribute<McpServerToolAttribute>()?.Name)
            .OfType<string>()
            .ToArray();

    private static string FindRealRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Meridian.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not locate the Meridian repository root.");
    }
}
