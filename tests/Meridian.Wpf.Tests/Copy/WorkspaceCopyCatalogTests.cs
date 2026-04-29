using System.Text.RegularExpressions;
using Meridian.Wpf.Copy;

namespace Meridian.Wpf.Tests.Copy;

public sealed class WorkspaceCopyCatalogTests
{
    private static readonly Regex CopyKeyPattern = new(
        "^[a-z0-9-]+\\.[a-z0-9-]+\\.[a-z0-9-]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Fact]
    public void CopyEntries_ShouldUseWorkspaceSectionIntentKeys()
    {
        foreach (var entry in WorkspaceCopyCatalog.Entries)
        {
            CopyKeyPattern.IsMatch(entry.Key)
                .Should()
                .BeTrue($"copy key '{entry.Key}' must follow workspace.section.intent");
        }
    }

    [Fact]
    public void CopyEntries_ShouldNotContainConflictingDuplicateKeys()
    {
        var conflicting = WorkspaceCopyCatalog.Entries
            .GroupBy(entry => entry.Key, StringComparer.Ordinal)
            .Where(group => group.Select(entry => entry.Text).Distinct(StringComparer.Ordinal).Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        conflicting.Should().BeEmpty("copy keys should not map to multiple conflicting values");
    }

    [Fact]
    public void SubtitleEntries_ShouldStayWithinLengthBudget()
    {
        const int maxSubtitleLength = 120;

        var overlong = WorkspaceCopyCatalog.Entries
            .Where(entry => entry.Key.Contains("subtitle", StringComparison.Ordinal))
            .Where(entry => entry.Text.Length > maxSubtitleLength)
            .Select(entry => $"{entry.Key} ({entry.Text.Length})")
            .ToArray();

        overlong.Should().BeEmpty($"workspace subtitles must be <= {maxSubtitleLength} characters");
    }
}
