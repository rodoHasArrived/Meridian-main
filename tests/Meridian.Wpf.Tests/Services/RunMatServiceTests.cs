using System.IO;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.Services;

public sealed class RunMatServiceTests
{
    [Fact]
    public async Task SaveScriptAsync_ShouldPersistScriptAndListIt()
    {
        var root = Path.Combine(Path.GetTempPath(), "runmat-service-test-" + Guid.NewGuid().ToString("N"));
        var service = new RunMatService(root);

        var scriptPath = await service.SaveScriptAsync("alpha_test", "disp(42);");
        var scripts = await service.GetScriptsAsync();

        scriptPath.Should().EndWith(".m");
        File.Exists(scriptPath).Should().BeTrue();
        scripts.Should().Contain(script => string.Equals(script.Path, scriptPath, StringComparison.OrdinalIgnoreCase));
        (await service.LoadScriptAsync(scriptPath)).Should().Contain("disp(42);");
    }

    [Fact]
    public async Task GetStatusAsync_WithoutExecutable_ShouldReportNotInstalled()
    {
        var root = Path.Combine(Path.GetTempPath(), "runmat-status-test-" + Guid.NewGuid().ToString("N"));
        var service = new RunMatService(root);

        var status = await service.GetStatusAsync();

        status.IsInstalled.Should().Be(!string.IsNullOrWhiteSpace(status.ResolvedExecutablePath));
        status.StatusMessage.Should().NotBeNullOrWhiteSpace();
        Directory.Exists(status.ScriptsDirectory).Should().BeTrue();
    }

    [Fact]
    public void ResolveExecutablePath_WithExplicitExistingPath_ShouldReturnIt()
    {
        var root = Path.Combine(Path.GetTempPath(), "runmat-resolve-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var executablePath = Path.Combine(root, "runmat.exe");
        File.WriteAllText(executablePath, string.Empty);

        var service = new RunMatService(root);

        service.ResolveExecutablePath(executablePath).Should().Be(executablePath);
    }
}
