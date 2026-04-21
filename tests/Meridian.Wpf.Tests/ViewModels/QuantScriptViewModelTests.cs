#if WINDOWS
using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Meridian.QuantScript;
using Meridian.QuantScript.Compilation;
using Meridian.QuantScript.Documents;
using Meridian.QuantScript.Plotting;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

/// <summary>
/// Unit-tests for <see cref="QuantScriptViewModel"/> that do not require a live UI thread.
/// Timer-dependent and file-watcher-dependent behaviour is exercised via the public command surface.
/// </summary>
public sealed class QuantScriptViewModelTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private sealed class StubLayoutService : IQuantScriptLayoutService
    {
        public (double ChartHeight, double EditorHeight) LoadRowHeights() => (300, 400);
        public void SaveRowHeights(double chartHeight, double editorHeight) { }
        public (double LeftWidth, double RightWidth) LoadColumnWidths() => (300, 400);
        public void SaveColumnWidths(double l, double r) { }
        public int LoadLastActiveTab() => 0;
        public void SaveLastActiveTab(int tabIndex) { }
    }

    private static QuantScriptViewModel CreateVm(
        IScriptRunner? runner = null,
        IQuantScriptCompiler? compiler = null)
    {
        var fakeRunner   = runner   ?? new FakeScriptRunner();
        var fakeCompiler = compiler ?? new FakeQuantScriptCompiler();
        var plotQueue    = new PlotQueue();
        var layout       = new StubLayoutService();
        var options      = Options.Create(new QuantScriptOptions { ScriptsDirectory = Path.GetTempPath() });
        var notebookStore = new QuantScriptNotebookStore(options.Value);
        var logger       = NullLogger<QuantScriptViewModel>.Instance;

        return new QuantScriptViewModel(fakeRunner, fakeCompiler, plotQueue, layout, notebookStore, options, logger);
    }

    // ── Initial state ─────────────────────────────────────────────────────────

    [Fact]
    public void InitialState_IsRunning_IsFalse()
    {
        var vm = CreateVm();
        vm.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void InitialState_CanRun_IsTrue()
    {
        var vm = CreateVm();
        vm.CanRun.Should().BeTrue();
    }

    [Fact]
    public void InitialState_StatusText_IsReady()
    {
        var vm = CreateVm();
        vm.StatusText.Should().Be("Ready");
    }

    [Fact]
    public void InitialState_ConsoleOutput_IsEmpty()
    {
        var vm = CreateVm();
        vm.ConsoleOutput.Should().BeEmpty();
    }

    [Fact]
    public void InitialState_Metrics_IsEmpty()
    {
        var vm = CreateVm();
        vm.Metrics.Should().BeEmpty();
    }

    // ── Tab header computed properties ────────────────────────────────────────

    [Fact]
    public void ConsoleTabHeader_WhenEmpty_ShowsPlainLabel()
    {
        var vm = CreateVm();
        vm.ConsoleTabHeader.Should().Be("Console");
    }

    [Fact]
    public void MetricsTabHeader_WhenEmpty_ShowsPlainLabel()
    {
        var vm = CreateVm();
        vm.MetricsTabHeader.Should().Be("Metrics");
    }

    [Fact]
    public void ChartsTabHeader_WhenEmpty_ShowsPlainLabel()
    {
        var vm = CreateVm();
        vm.ChartsTabHeader.Should().Be("Charts");
    }

    // ── ScriptSource property ─────────────────────────────────────────────────

    [Fact]
    public void ScriptSource_SetValue_RaisesPropertyChanged()
    {
        var vm = CreateVm();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.ScriptSource = "// test";

        raised.Should().Contain(nameof(vm.ScriptSource));
    }

    // ── ClearConsole command ──────────────────────────────────────────────────

    [Fact]
    public void ClearConsoleCommand_Always_CanExecute()
    {
        var vm = CreateVm();
        vm.ClearConsoleCommand.CanExecute(null).Should().BeTrue();
    }

    // ── NewScript command ─────────────────────────────────────────────────────

    [Fact]
    public void NewScriptCommand_Execute_ClearsScriptSource()
    {
        var vm = CreateVm();
        vm.ScriptSource = "old content";

        vm.NewScriptCommand.Execute(null);

        vm.ScriptSource.Should().Contain("New QuantScript");
    }

    // ── Dispose ───────────────────────────────────────────────────────────────

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var vm = CreateVm();
        var act = () =>
        {
            vm.Dispose();
            vm.Dispose();
        };
        act.Should().NotThrow();
    }

    [Fact]
    public async Task RunAll_WhenCellFails_LeavesProgressBelowOne()
    {
        var runner = new FakeScriptRunner().SetResult(new ScriptRunResult(
            Success: false,
            Elapsed: TimeSpan.FromMilliseconds(50),
            CompileTime: TimeSpan.FromMilliseconds(10),
            PeakMemoryBytes: 0,
            CompilationErrors: Array.Empty<ScriptDiagnostic>(),
            RuntimeError: "boom",
            ConsoleOutput: "error",
            Metrics: Array.Empty<KeyValuePair<string, string>>(),
            Plots: Array.Empty<PlotRequest>(),
            TradesSummary: Array.Empty<string>()));
        var vm = CreateVm(runner: runner);

        var succeeded = await vm.RunAllCommand.ExecuteAsync(null);

        succeeded.Should().BeFalse();
        vm.ProgressFraction.Should().BeLessThan(1.0);
        vm.StatusText.Should().NotStartWith("Completed");
    }

    [Fact]
    public async Task RunAll_WhenCancelled_LeavesProgressBelowOne()
    {
        var runner = new FakeScriptRunner().SetException(new OperationCanceledException());
        var vm = CreateVm(runner: runner);

        var succeeded = await vm.RunAllCommand.ExecuteAsync(null);

        succeeded.Should().BeFalse();
        vm.StatusText.Should().Be("Cancelled");
        vm.ProgressFraction.Should().BeLessThan(1.0);
    }
}

/// <summary>
/// No-op compiler for ViewModel tests that do not need real Roslyn compilation.
/// </summary>
internal sealed class FakeQuantScriptCompiler : IQuantScriptCompiler
{
    public Task<ScriptCompilationResult> CompileAsync(string source, CancellationToken ct = default)
        => Task.FromResult(new ScriptCompilationResult(
            Success: true,
            CompilationTime: TimeSpan.FromMilliseconds(1),
            Diagnostics: Array.Empty<ScriptDiagnostic>()));

    public IReadOnlyList<ParameterDescriptor> ExtractParameters(string source)
        => Array.Empty<ParameterDescriptor>();
}
#endif
