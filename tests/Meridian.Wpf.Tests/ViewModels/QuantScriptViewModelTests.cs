#if WINDOWS
using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Meridian.QuantScript;
using Meridian.QuantScript.Compilation;
using Meridian.QuantScript.Documents;
using Meridian.QuantScript.Plotting;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.Models;
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
        var templateCatalog = new QuantScriptTemplateCatalogService(NullLogger<QuantScriptTemplateCatalogService>.Instance);
        var strategyRunWorkspace = new StrategyRunWorkspaceService(
            new StrategyRunStore(),
            new PortfolioReadService(),
            new LedgerReadService());
        var executionHistory = new QuantScriptExecutionHistoryService(
            ConfigService.Instance,
            strategyRunWorkspace,
            NullLogger<QuantScriptExecutionHistoryService>.Instance);
        var logger       = NullLogger<QuantScriptViewModel>.Instance;

        return new QuantScriptViewModel(
            fakeRunner,
            fakeCompiler,
            plotQueue,
            layout,
            notebookStore,
            templateCatalog,
            executionHistory,
            NavigationService.Instance,
            options,
            logger);
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

    [Fact]
    public void RunHistoryPresentation_WhenEmpty_ShowsEmptyStateAndDisablesHandoffs()
    {
        var vm = CreateVm();

        vm.RunHistoryTabHeader.Should().Be("Run History");
        vm.RunHistoryScopeText.Should().Be("No execution history");
        vm.HasRunHistory.Should().BeFalse();
        vm.HasNoRunHistory.Should().BeTrue();
        vm.HistoryEmptyStateTitle.Should().Be("No QuantScript execution history yet");
        vm.HistoryEmptyStateDetail.Should().Contain("Run a cell or notebook");
        vm.SelectedHistoryTitle.Should().Be("No history entry selected");
        vm.SelectedHistoryDetail.Should().Contain("Select a history entry");
        vm.SelectedHistoryEvidenceText.Should().Be("No execution evidence selected");
        vm.OpenRunBrowserCommand.CanExecute(null).Should().BeFalse();
        vm.OpenRunDetailCommand.CanExecute(null).Should().BeFalse();
        vm.CompareInResearchCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void SelectedExecutionRecord_WithMirroredRun_EnablesRunHistoryHandoffs()
    {
        var vm = CreateVm();
        var record = MakeHistoryRecord(mirroredRunId: "run-quant-1");

        vm.RunHistory.Add(record);
        vm.SelectedExecutionRecord = record;

        vm.RunHistoryTabHeader.Should().Be("Run History (1)");
        vm.RunHistoryScopeText.Should().Be("1 execution record");
        vm.HasRunHistory.Should().BeTrue();
        vm.HasNoRunHistory.Should().BeFalse();
        vm.SelectedHistoryTitle.Should().Be("Momentum Notebook");
        vm.SelectedHistoryDetail.Should().Contain("Success Notebook");
        vm.SelectedHistoryEvidenceText.Should().Be("1 metric | 1 plot | 1 mirrored backtest");
        vm.SelectedHistoryRunLinkText.Should().Contain("run-quant-1");
        vm.SelectedHistoryParameterText.Should().Contain("symbol=SPY");
        vm.SelectedHistoryConsolePreview.Should().Be("Loaded 42 bars");
        vm.OpenRunBrowserCommand.CanExecute(null).Should().BeTrue();
        vm.OpenRunDetailCommand.CanExecute(null).Should().BeTrue();
        vm.CompareInResearchCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void SelectedExecutionRecord_WithoutMirroredRun_DisablesRunHistoryHandoffs()
    {
        var vm = CreateVm();
        var record = MakeHistoryRecord(mirroredRunId: null);

        vm.RunHistory.Add(record);
        vm.SelectedExecutionRecord = record;

        vm.SelectedHistoryEvidenceText.Should().Be("1 metric | 1 plot | 0 mirrored backtests");
        vm.SelectedHistoryRunLinkText.Should().Be("Local execution only; no Strategy Runs handoff was recorded.");
        vm.OpenRunBrowserCommand.CanExecute(null).Should().BeFalse();
        vm.OpenRunDetailCommand.CanExecute(null).Should().BeFalse();
        vm.CompareInResearchCommand.CanExecute(null).Should().BeFalse();
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

        vm.ScriptSource.Should().Contain("Data.PricesAsync");
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
            TradesSummary: Array.Empty<string>(),
            CapturedBacktests: Array.Empty<Meridian.Backtesting.Sdk.BacktestResult>(),
            RuntimeParameters: Array.Empty<ParameterDescriptor>()));
        var vm = CreateVm(runner: runner);

        await vm.RunAllCommand.ExecuteAsync(null);

        vm.ProgressFraction.Should().BeLessThan(1.0);
        vm.StatusText.Should().NotStartWith("Completed");
    }

    [Fact]
    public async Task RunAll_WhenCancelled_LeavesProgressBelowOne()
    {
        var runner = new FakeScriptRunner().SetException(new OperationCanceledException());
        var vm = CreateVm(runner: runner);

        await vm.RunAllCommand.ExecuteAsync(null);

        vm.StatusText.Should().Be("Cancelled");
        vm.ProgressFraction.Should().BeLessThan(1.0);
    }

    [Fact]
    public async Task RunScriptCommand_WhenDateRangeInvalid_BlocksRunAndReportsStatus()
    {
        var runner = new FakeScriptRunner();
        var vm = CreateVm(runner: runner);
        vm.FromDate = new DateTime(2025, 1, 2);
        vm.ToDate = new DateTime(2025, 1, 1);

        await vm.RunScriptCommand.ExecuteAsync(null);

        runner.CallCount.Should().Be(0);
        vm.StatusText.Should().Contain("Invalid date range");
        vm.Diagnostics.Should().Contain(entry => entry.Key == "Validation");
    }

    [Fact]
    public async Task RunAndAdvanceCommand_WhenDateRangeInvalid_BlocksRunAndReportsStatus()
    {
        var runner = new FakeScriptRunner();
        var vm = CreateVm(runner: runner);
        vm.FromDate = new DateTime(2025, 1, 2);
        vm.ToDate = new DateTime(2025, 1, 1);

        await vm.RunAndAdvanceCommand.ExecuteAsync(null);

        runner.CallCount.Should().Be(0);
        vm.StatusText.Should().Contain("Invalid date range");
        vm.Diagnostics.Should().Contain(entry => entry.Key == "Validation");
    }

    [Fact]
    public async Task RunScriptCommand_IncludesNormalizedToolbarContextInParameters()
    {
        var runner = new FakeScriptRunner();
        var vm = CreateVm(runner: runner);
        vm.AssetSymbol = " spy ";
        vm.FromDate = new DateTime(2024, 1, 2);
        vm.ToDate = new DateTime(2024, 2, 3);
        vm.SelectedInterval = "Daily (Custom)";

        await vm.RunScriptCommand.ExecuteAsync(null);

        runner.CallCount.Should().Be(1);
        runner.LastParameters.Should().NotBeNull();
        runner.LastParameters!["symbol"].Should().Be("SPY");
        runner.LastParameters["from"].Should().Be(new DateOnly(2024, 1, 2));
        runner.LastParameters["to"].Should().Be(new DateOnly(2024, 2, 3));
        runner.LastParameters["interval"].Should().Be("daily");
    }

    [Fact]
    public void QuantScriptPageSource_BindsRunHistoryTab()
    {
        var xaml = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Views\QuantScriptPage.xaml"));

        xaml.Should().Contain("QuantScriptRunHistoryTab");
        xaml.Should().Contain("{Binding RunHistoryTabHeader}");
        xaml.Should().Contain("{Binding RunHistoryScopeText}");
        xaml.Should().Contain("QuantScriptRunHistoryGrid");
        xaml.Should().Contain("{Binding RunHistory}");
        xaml.Should().Contain("{Binding SelectedExecutionRecord, Mode=TwoWay}");
        xaml.Should().Contain("{Binding OpenRunBrowserCommand}");
        xaml.Should().Contain("{Binding OpenRunDetailCommand}");
        xaml.Should().Contain("{Binding CompareInResearchCommand}");
        xaml.Should().Contain("{Binding HasNoRunHistory");
        xaml.Should().Contain("{Binding SelectedHistoryConsolePreview}");
    }

    private static QuantScriptExecutionRecord MakeHistoryRecord(string? mirroredRunId)
        => new(
            ExecutionId: Guid.NewGuid().ToString("N"),
            DocumentTitle: "Momentum Notebook",
            DocumentPath: @"C:\Meridian\quant\momentum.qsnb",
            DocumentKind: QuantScriptDocumentKind.Notebook,
            ExecutedAtUtc: new DateTimeOffset(2026, 4, 26, 16, 0, 0, TimeSpan.Zero),
            Success: true,
            ParameterSnapshot: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["symbol"] = "SPY"
            },
            RuntimeParameters: [],
            ConsoleExcerpt: "Loaded 42 bars",
            Metrics: [new QuantScriptExecutionMetricRecord("Sharpe", "1.23", "Risk")],
            PlotTitles: ["Equity Curve"],
            CapturedBacktestCount: mirroredRunId is null ? 0 : 1,
            MirroredRunId: mirroredRunId);

    private static string GetRepositoryFilePath(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException($"Could not locate repository file '{relativePath}' from '{AppContext.BaseDirectory}'.");
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
