using System.Text.RegularExpressions;
using FluentAssertions;
using Meridian.Application.Commands;
using Xunit;

namespace Meridian.Tests.Application.Commands;

/// <summary>
/// Tests for the HelpCommand CLI handler.
/// </summary>
public sealed class HelpCommandTests
{
    [Fact]
    public void CanHandle_WithHelpFlag_ReturnsTrue()
    {
        var cmd = new HelpCommand();
        cmd.CanHandle(new[] { "--help" }).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_WithShortHelpFlag_ReturnsTrue()
    {
        var cmd = new HelpCommand();
        cmd.CanHandle(new[] { "-h" }).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_CaseInsensitive_ReturnsTrue()
    {
        var cmd = new HelpCommand();
        cmd.CanHandle(new[] { "--HELP" }).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_WithOtherFlags_ReturnsFalse()
    {
        var cmd = new HelpCommand();
        cmd.CanHandle(new[] { "--selftest" }).Should().BeFalse();
    }

    [Fact]
    public void CanHandle_EmptyArgs_ReturnsFalse()
    {
        var cmd = new HelpCommand();
        cmd.CanHandle(Array.Empty<string>()).Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsZero()
    {
        var cmd = new HelpCommand();
        var result = await cmd.ExecuteAsync(new[] { "--help" });
        result.ExitCode.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WithMixedArgs_ReturnsZero()
    {
        var cmd = new HelpCommand();
        var result = await cmd.ExecuteAsync(new[] { "--help", "--verbose" });
        result.ExitCode.Should().Be(0);
    }

    [Fact]
    public void HelpText_CitedRepositoryPaths_AllExist()
    {
        // Help text is printed verbatim to operators, so a renamed or deleted doc silently
        // becomes a dead pointer. Scan the source for repo-relative .md/.cs paths it cites.
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root, "src", "Meridian.Application", "Commands", "HelpCommand.cs"));

        var cited = Regex.Matches(source, @"(?<![\w/.-])(?:docs|src)/[A-Za-z0-9_./-]+\.(?:md|cs)|(?<![\w/.-])README\.md")
            .Select(m => m.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        cited.Should().NotBeEmpty();
        cited.Where(path => !File.Exists(Path.Combine(root, path)))
            .Should().BeEmpty("every path HelpCommand prints must exist in the repository");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Meridian.sln")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Unable to locate Meridian repository root from test output directory.");
    }
}
