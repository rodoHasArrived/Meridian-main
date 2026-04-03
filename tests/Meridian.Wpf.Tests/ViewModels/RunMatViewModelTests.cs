using System.IO;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class RunMatViewModelTests
{
    // ── Helpers ───────────────────────────────────────────────────────────

    private static (RunMatViewModel vm, RunMatService service, string root) CreateSubject()
    {
        var root = Path.Combine(Path.GetTempPath(), "runmat-vm-test-" + Guid.NewGuid().ToString("N"));
        var service = new RunMatService(root);
        var vm = new RunMatViewModel(service);
        return (vm, service, root);
    }

    // ── Computed property tests ────────────────────────────────────────────

    [Fact]
    public void CanRun_WhenNotRunning_ShouldBeTrue()
    {
        var (vm, _, _) = CreateSubject();

        vm.IsRunning.Should().BeFalse();
        vm.CanRun.Should().BeTrue();
    }

    [Fact]
    public void CanRun_WhenIsRunningSetTrue_ShouldBeFalse()
    {
        var (vm, _, _) = CreateSubject();

        vm.IsRunning = true;

        vm.CanRun.Should().BeFalse();
    }

    [Fact]
    public void CanRun_WhenIsRunningResetToFalse_ShouldBeTrue()
    {
        var (vm, _, _) = CreateSubject();
        vm.IsRunning = true;

        vm.IsRunning = false;

        vm.CanRun.Should().BeTrue();
    }

    // ── NewScript command tests ────────────────────────────────────────────

    [Fact]
    public void NewScriptCommand_Execute_ShouldClearSelectedScriptAndSetScratchName()
    {
        var (vm, _, _) = CreateSubject();

        vm.NewScriptCommand.Execute(null);

        vm.SelectedScript.Should().BeNull();
        vm.ScriptName.Should().MatchRegex(@"scratch_\d{6}\.m");
        vm.ScriptSource.Should().Contain("linspace");
        vm.StatusText.Should().Be("Created new scratch script.");
    }

    [Fact]
    public void NewScriptCommand_ExecutedTwice_ShouldProduceDifferentScriptNames()
    {
        var (vm, _, _) = CreateSubject();

        vm.NewScriptCommand.Execute(null);
        var first = vm.ScriptName;

        vm.NewScriptCommand.Execute(null);
        var second = vm.ScriptName;

        second.Should().NotBe(first, "each new script gets a unique counter-based name");
    }

    // ── StopRun command tests ─────────────────────────────────────────────

    [Fact]
    public void StopRunCommand_WhenNotRunning_ShouldNotThrow()
    {
        var (vm, _, _) = CreateSubject();

        var act = () => vm.StopRunCommand.Execute(null);

        act.Should().NotThrow();
    }

    // ── InitializeAsync tests ─────────────────────────────────────────────

    [Fact]
    public async Task InitializeAsync_WithEmptyService_ShouldResultInNewScratchScript()
    {
        var (vm, _, _) = CreateSubject();

        await vm.InitializeAsync();

        vm.Scripts.Should().NotBeEmpty();
        vm.SelectedScript.Should().NotBeNull();
        vm.ScriptName.Should().EndWith(".m");
        vm.StatusText.Should().Be("RunMat Lab ready.");
    }

    [Fact]
    public async Task InitializeAsync_WithExistingScript_ShouldLoadLastScript()
    {
        var (vm, service, _) = CreateSubject();
        await service.SaveScriptAsync("hello.m", "disp('hello');");

        await vm.InitializeAsync();

        vm.Scripts.Should().ContainSingle(s => s.Name == "hello.m");
        vm.SelectedScript.Should().NotBeNull();
        vm.ScriptSource.Should().Contain("disp('hello')");
        vm.StatusText.Should().Be("RunMat Lab ready.");
    }

    // ── Script lifecycle tests ────────────────────────────────────────────

    [Fact]
    public async Task SaveScriptCommand_ShouldPersistScriptAndPopulateScriptsList()
    {
        var (vm, _, _) = CreateSubject();
        vm.ScriptName = "my_script.m";
        vm.ScriptSource = "x = 1 + 2;";

        await vm.SaveScriptCommand.ExecuteAsync(null);

        vm.Scripts.Should().ContainSingle(s => s.Name == "my_script.m");
        vm.SelectedScript.Should().NotBeNull();
        vm.StatusText.Should().Be("Saved my_script.m.");
    }

    [Fact]
    public async Task RefreshScriptsCommand_AfterSave_ShouldReflectNewScript()
    {
        var (vm, service, _) = CreateSubject();
        await service.SaveScriptAsync("direct.m", "a = 42;");

        await vm.RefreshScriptsCommand.ExecuteAsync(null);

        vm.Scripts.Should().ContainSingle(s => s.Name == "direct.m");
    }

    // ── Dispose ───────────────────────────────────────────────────────────

    [Fact]
    public void Dispose_WhenNotRunning_ShouldNotThrow()
    {
        var (vm, _, _) = CreateSubject();

        var act = () => vm.Dispose();

        act.Should().NotThrow();
    }

    [Fact]
    public void Dispose_WhenCalledTwice_ShouldNotThrow()
    {
        var (vm, _, _) = CreateSubject();

        vm.Dispose();
        var act = () => vm.Dispose();

        act.Should().NotThrow();
    }
}
