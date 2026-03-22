using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Meridian.Tests.Application.Canonicalization;

public sealed class CanonicalizationFixtureDriftTests
{
    private static readonly string RepoRoot = ResolveRepoRoot();
    private static readonly string FixturesDir = Path.Combine(
        RepoRoot,
        "tests", "Meridian.Tests", "Application", "Canonicalization", "Fixtures");

    [Fact]
    public void CanonicalizationGoldenFixture_DriftReport_HasNoUnmappedFixtureCodesOrVenues()
    {
        var conditionMappings = LoadMappings(Path.Combine(RepoRoot, "config", "condition-codes.json"));
        var venueMappings = LoadMappings(Path.Combine(RepoRoot, "config", "venue-mapping.json"));

        var missingConditions = new SortedSet<string>(StringComparer.Ordinal);
        var missingVenues = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var fixturePath in Directory.GetFiles(FixturesDir, "*.json").OrderBy(static path => path, StringComparer.Ordinal))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(fixturePath));
            var raw = doc.RootElement.GetProperty("raw");

            var provider = raw.GetProperty("source").GetString()?.ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(provider))
            {
                continue;
            }

            if (raw.TryGetProperty("conditions", out var conditions) && conditions.ValueKind == JsonValueKind.Array)
            {
                foreach (var condition in conditions.EnumerateArray().Select(static value => value.GetString()).Where(static value => !string.IsNullOrWhiteSpace(value)))
                {
                    if (!conditionMappings.Contains($"{provider}:{condition}"))
                    {
                        missingConditions.Add($"{provider}:{condition}");
                    }
                }
            }

            if (raw.TryGetProperty("venue", out var venue) && venue.ValueKind == JsonValueKind.String)
            {
                var venueValue = venue.GetString();
                if (!string.IsNullOrWhiteSpace(venueValue) && !venueMappings.Contains($"{provider}:{venueValue}"))
                {
                    missingVenues.Add($"{provider}:{venueValue}");
                }
            }
        }

        var report = BuildFailureMessage(missingConditions, missingVenues);
        missingConditions.Should().BeEmpty(report);
        missingVenues.Should().BeEmpty(report);
    }

    private static HashSet<string> LoadMappings(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var result = new HashSet<string>(StringComparer.Ordinal);

        foreach (var provider in doc.RootElement.GetProperty("mappings").EnumerateObject())
        {
            foreach (var entry in provider.Value.EnumerateObject())
            {
                result.Add($"{provider.Name.ToUpperInvariant()}:{entry.Name}");
            }
        }

        return result;
    }

    private static string BuildFailureMessage(
        IEnumerable<string> missingConditions,
        IEnumerable<string> missingVenues)
    {
        var conditionList = missingConditions.ToArray();
        var venueList = missingVenues.ToArray();

        if (conditionList.Length == 0 && venueList.Length == 0)
        {
            return string.Empty;
        }

        return "Fixture drift detected.\n"
            + $"Missing condition mappings: {(conditionList.Length == 0 ? "none" : string.Join(", ", conditionList))}\n"
            + $"Missing venue mappings: {(venueList.Length == 0 ? "none" : string.Join(", ", venueList))}\n"
            + "Update config/condition-codes.json, config/venue-mapping.json, or refresh the affected fixture files.";
    }

    private static string ResolveRepoRoot()
    {
        var candidate = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);
        while (candidate is not null && !File.Exists(Path.Combine(candidate.FullName, "Meridian.sln")))
        {
            candidate = candidate.Parent;
        }

        return candidate?.FullName ?? AppContext.BaseDirectory;
    }
}
