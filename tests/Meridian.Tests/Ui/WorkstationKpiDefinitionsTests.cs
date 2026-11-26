using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Services;
using Xunit;

namespace Meridian.Tests.Ui;

/// <summary>
/// The workstation headline-figure catalog (UX Phase A) against the fixture the browser's
/// lib/kpi-registry.contract.test.ts also reads, so both clients state each figure identically.
/// </summary>
public sealed class WorkstationKpiDefinitionsTests
{
    [Fact]
    public void Catalog_MatchesTheSharedFixtureExactly()
    {
        var expected = LoadFixture();

        WorkstationKpiDefinitions.All.Should().BeEquivalentTo(expected, options => options.WithStrictOrdering());
    }

    [Fact]
    public void Catalog_GivesEveryFigureAUniqueIdAndAPopulation()
    {
        WorkstationKpiDefinitions.All.Select(static definition => definition.Id).Should().OnlyHaveUniqueItems();
        WorkstationKpiDefinitions.All.Should().OnlyContain(static definition =>
            !string.IsNullOrWhiteSpace(definition.Label) && !string.IsNullOrWhiteSpace(definition.Population));
    }

    [Fact]
    public void ProviderHealthFigures_CoverTheSamePopulationOnEveryScreen()
    {
        var providers = WorkstationKpiDefinitions.All
            .Where(static definition => definition.Id is "system.providers-online" or "data.providers-healthy")
            .ToArray();

        providers.Should().HaveCount(2);
        providers.Select(static definition => (definition.Label, definition.Population)).Distinct().Should().ContainSingle();
    }

    [Fact]
    public void Route_IsTheWorkstationKpiDefinitionsPath()
    {
        UiApiRoutes.WorkstationKpiDefinitions.Should().Be("/api/workstation/kpi-definitions");
    }

    private static IReadOnlyList<WorkstationKpiDefinitionDto> LoadFixture()
    {
        var copied = Path.Combine(AppContext.BaseDirectory, "fixtures", "kpi-definitions", "kpi-definitions.json");
        var path = File.Exists(copied) ? copied : Path.Combine(FindRepoRoot(), "tests", "fixtures", "kpi-definitions", "kpi-definitions.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty("definitions").EnumerateArray()
            .Select(static item => new WorkstationKpiDefinitionDto(
                item.GetProperty("id").GetString()!,
                item.GetProperty("label").GetString()!,
                item.GetProperty("population").GetString()!,
                item.GetProperty("comparisonBasis").ValueKind == JsonValueKind.Null ? null : item.GetProperty("comparisonBasis").GetString()))
            .ToArray();
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Meridian.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root (Meridian.sln) not found.");
    }
}
