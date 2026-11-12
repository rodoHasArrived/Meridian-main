using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Services;
using Xunit;

namespace Meridian.Tests.Ui;

/// <summary>
/// The cross-screen count definitions (A3) against the fixture the browser's
/// lib/cross-screen-counts.test.ts also reads, so both clients count the same way.
/// </summary>
public sealed class CrossScreenCountDefinitionsTests
{
    private static readonly JsonDocument Fixture = LoadFixture();

    [Fact]
    public void CountProviderHealth_MatchesEveryFixtureCase()
    {
        foreach (var testCase in Fixture.RootElement.GetProperty("providerHealth").EnumerateArray())
        {
            var statuses = testCase.GetProperty("statuses").EnumerateArray().Select(static item => item.GetString());
            var (healthy, total) = CrossScreenCountDefinitions.CountProviderHealth(statuses);

            healthy.Should().Be(testCase.GetProperty("healthy").GetInt32(), testCase.GetProperty("name").GetString());
            total.Should().Be(testCase.GetProperty("total").GetInt32(), testCase.GetProperty("name").GetString());
        }
    }

    [Fact]
    public void IsOpenBreak_MatchesEveryFixtureCase()
    {
        foreach (var testCase in Fixture.RootElement.GetProperty("openBreaks").EnumerateArray())
        {
            var open = testCase.GetProperty("statuses").EnumerateArray()
                .Select(static item => Enum.Parse<ReconciliationBreakQueueStatus>(item.GetString()!))
                .Count(CrossScreenCountDefinitions.IsOpenBreak);

            open.Should().Be(testCase.GetProperty("open").GetInt32(), testCase.GetProperty("name").GetString());
        }
    }

    [Fact]
    public void BreakFixture_CoversEveryQueueStatus()
    {
        // A status added to the queue must be placed on one side of the definition, in the fixture,
        // before either client is changed.
        var covered = Fixture.RootElement.GetProperty("openBreaks").EnumerateArray()
            .SelectMany(static testCase => testCase.GetProperty("statuses").EnumerateArray())
            .Select(static item => item.GetString())
            .ToHashSet(StringComparer.Ordinal);

        covered.Should().BeEquivalentTo(Enum.GetNames<ReconciliationBreakQueueStatus>());
    }

    [Fact]
    public void IsOpenBreak_TreatsOnlyResolvedDismissedAndSignedOffAsClosed()
    {
        Enum.GetValues<ReconciliationBreakQueueStatus>()
            .Where(static status => !CrossScreenCountDefinitions.IsOpenBreak(status))
            .Should()
            .BeEquivalentTo([
                ReconciliationBreakQueueStatus.Resolved,
                ReconciliationBreakQueueStatus.Dismissed,
                ReconciliationBreakQueueStatus.SignedOff
            ]);
    }

    private static JsonDocument LoadFixture()
    {
        var copied = Path.Combine(AppContext.BaseDirectory, "fixtures", "cross-screen-counts", "cross-screen-counts.json");
        var path = File.Exists(copied) ? copied : Path.Combine(FindRepoRoot(), "tests", "fixtures", "cross-screen-counts", "cross-screen-counts.json");
        return JsonDocument.Parse(File.ReadAllText(path));
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
