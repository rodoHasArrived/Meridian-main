using FluentAssertions;
using Meridian.Ui.Shared;

namespace Meridian.Tests.Ui;

public sealed class StaticAssetPathResolverTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "Meridian", "static-assets", Guid.NewGuid().ToString("N"));

    [Fact]
    public void ResolveWebRootPath_WhenExistingWebRootExists_ShouldPreferExistingWebRoot()
    {
        var existingWebRoot = Path.Combine(_tempRoot, "existing-wwwroot");
        var contentRoot = Path.Combine(_tempRoot, "repo");
        var appBaseDirectory = Path.Combine(_tempRoot, "bin", "Debug", "net9.0");

        Directory.CreateDirectory(existingWebRoot);
        Directory.CreateDirectory(contentRoot);
        Directory.CreateDirectory(Path.Combine(appBaseDirectory, "wwwroot"));

        var resolved = StaticAssetPathResolver.ResolveWebRootPath(existingWebRoot, contentRoot, appBaseDirectory);

        resolved.Should().Be(Path.GetFullPath(existingWebRoot));
    }

    [Fact]
    public void ResolveWebRootPath_WhenContentRootLacksAssets_ShouldUseAppBaseDirectoryWwwroot()
    {
        var contentRoot = Path.Combine(_tempRoot, "repo");
        var appBaseDirectory = Path.Combine(_tempRoot, "bin", "Debug", "net9.0");
        var expected = Path.Combine(appBaseDirectory, "wwwroot");

        Directory.CreateDirectory(contentRoot);
        Directory.CreateDirectory(Path.Combine(expected, "workstation"));
        File.WriteAllText(Path.Combine(expected, "workstation", "index.html"), "<html></html>");

        var resolved = StaticAssetPathResolver.ResolveWebRootPath(null, contentRoot, appBaseDirectory);

        resolved.Should().Be(Path.GetFullPath(expected));
    }

    [Fact]
    public void ResolveWebRootPath_WhenOnlyRepoProjectAssetsExist_ShouldFindProjectWwwroot()
    {
        var contentRoot = Path.Combine(_tempRoot, "repo");
        var appBaseDirectory = Path.Combine(_tempRoot, "bin", "Debug", "net9.0");
        var expected = Path.Combine(contentRoot, "src", "Meridian", "wwwroot");

        Directory.CreateDirectory(Path.Combine(expected, "workstation"));
        Directory.CreateDirectory(appBaseDirectory);
        File.WriteAllText(Path.Combine(expected, "workstation", "index.html"), "<html></html>");

        var resolved = StaticAssetPathResolver.ResolveWebRootPath(null, contentRoot, appBaseDirectory);

        resolved.Should().Be(Path.GetFullPath(expected));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup for temp test artifacts.
        }
    }
}
