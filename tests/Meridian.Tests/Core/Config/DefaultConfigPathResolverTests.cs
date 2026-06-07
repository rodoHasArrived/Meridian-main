using FluentAssertions;
using Meridian.Application.Config;
using Xunit;

namespace Meridian.Tests.Core.Config;

public sealed class DefaultConfigPathResolverTests : IDisposable
{
    private readonly List<string> _tempDirectories = [];

    [Fact]
    public void ResolveFrom_PrefersConfigDirectoryInNearestAncestor()
    {
        var repositoryRoot = CreateTempDirectory();
        var configDirectory = Path.Combine(repositoryRoot, "config");
        Directory.CreateDirectory(configDirectory);
        File.WriteAllText(Path.Combine(configDirectory, "appsettings.json"), "{}");
        File.WriteAllText(Path.Combine(repositoryRoot, "appsettings.json"), "{}");
        var nestedDirectory = Path.Combine(repositoryRoot, "src", "Meridian.Application");
        Directory.CreateDirectory(nestedDirectory);

        var resolved = DefaultConfigPathResolver.ResolveFrom(nestedDirectory);

        resolved.Should().Be(Path.Combine(configDirectory, "appsettings.json"));
    }

    [Fact]
    public void ResolveFrom_UsesRootAppsettingsWhenConfigDirectoryIsMissing()
    {
        var repositoryRoot = CreateTempDirectory();
        var rootConfigPath = Path.Combine(repositoryRoot, "appsettings.json");
        File.WriteAllText(rootConfigPath, "{}");
        var nestedDirectory = Path.Combine(repositoryRoot, "src", "Meridian.Application");
        Directory.CreateDirectory(nestedDirectory);

        var resolved = DefaultConfigPathResolver.ResolveFrom(nestedDirectory);

        resolved.Should().Be(rootConfigPath);
    }

    [Fact]
    public void ResolveFrom_ReturnsStartDirectoryAppsettingsWhenNoCandidateExists()
    {
        var startDirectory = CreateTempDirectory();

        var resolved = DefaultConfigPathResolver.ResolveFrom(startDirectory);

        resolved.Should().Be(Path.Combine(startDirectory, "appsettings.json"));
    }

    public void Dispose()
    {
        foreach (var path in _tempDirectories.Where(Directory.Exists))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "meridian-config-path-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        _tempDirectories.Add(path);
        return path;
    }
}
