#if WINDOWS
using System.IO;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Meridian.Contracts.SecurityMaster;
using Meridian.QuantScript;
using Meridian.QuantScript.Api;
using Meridian.QuantScript.Compilation;
using Meridian.QuantScript.Documents;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class QuantScriptViewModelTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "meridian-wpf-quantscript-tests", Guid.NewGuid().ToString("N"));

    private sealed class StubLayoutService : IQuantScriptLayoutService
    {
        public (double ChartHeight, double EditorHeight) LoadRowHeights() => (300, 280);
        public void SaveRowHeights(double chartHeight, double editorHeight) { }
        public int LoadLastActiveTab() => 0;
        public void SaveLastActiveTab(int tabIndex) { }
    }

    private sealed class StubQuantDataContext : IQuantDataContext
    {
        public Task<PriceSeries> PricesAsync(string symbol, DateOnly from, DateOnly to, CancellationToken ct = default)
            => Task.FromResult(new PriceSeries(symbol, [new PriceBar(from, 10, 11, 9, 10, 1000)]));

        public Task<PriceSeries> PricesAsync(string symbol, DateOnly from, DateOnly to, string? provider, CancellationToken ct = default)
            => PricesAsync(symbol, from, to, ct);

        public Task<IReadOnlyList<ScriptTrade>> TradesAsync(string symbol, DateOnly date, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ScriptTrade>>(Array.Empty<ScriptTrade>());

        public Task<ScriptOrderBook?> OrderBookAsync(string symbol, DateTimeOffset timestamp, CancellationToken ct = default)
            => Task.FromResult<ScriptOrderBook?>(null);

        public Task<SecurityDetailDto?> SecMasterAsync(string symbol, CancellationToken ct = default)
            => Task.FromResult<SecurityDetailDto?>(null);

        public Task<IReadOnlyList<CorporateActionDto>> CorporateActionsAsync(string symbol, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CorporateActionDto>>(Array.Empty<CorporateActionDto>());
    }

    private sealed class BackgroundThreadScriptRunner(IScriptRunner innerRunner) : IScriptRunner
    {
        private readonly IScriptRunner _innerRunner = innerRunner;

        public async Task<ScriptRunResult> RunAsync(
            string source,
            IReadOnlyDictionary<string, object?> parameters,
            ScriptExecutionCheckpoint? previousCheckpoint = null,
            CancellationToken ct = default)
        {
            var result = await _innerRunner
                .RunAsync(source, parameters, previousCheckpoint, ct)
                .ConfigureAwait(false);

            return await Task.Run(() => result, ct);
        }
    }

    private sealed class ParameterAwareCompiler(IReadOnlyList<ParameterDescriptor> descriptors) : IQuantScriptCompiler
    {
        private readonly IReadOnlyList<ParameterDescriptor> _descriptors = descriptors;

        public Task<ScriptCompilationResult> CompileAsync(string source, CancellationToken ct = default) =>
            Task.FromResult(new ScriptCompilationResult(true, TimeSpan.FromMilliseconds(1), Array.Empty<ScriptDiagnostic>()));

        public IReadOnlyList<ParameterDescriptor> ExtractParameters(string source) => _descriptors;
    }

    [Fact]
    public void NewNotebookCommand_CreatesSeededNotebook()
    {
        var vm = CreateVm();

        vm.NewNotebookCommand.Execute(null);

        vm.Cells.Should().ContainSingle();
        vm.SelectedCell.Should().Be(vm.Cells[0]);
        vm.Cells[0].SourceCode.Should().Contain("Data.Prices(\"SPY\")");
    }

    [Fact]
    public void AddCellBelowCommand_InsertsAndSelectsNewCell()
    {
        var vm = CreateVm();
        vm.NewNotebookCommand.Execute(null);
        var firstCell = vm.Cells[0];

        vm.AddCellBelowCommand.Execute(firstCell);

        vm.Cells.Should().HaveCount(2);
        vm.SelectedCell.Should().Be(vm.Cells[1]);
        vm.Cells[1].Status.Should().Be(QuantScriptCellStatus.NotRun);
    }

    [Fact]
    public void EditingEarlierCell_MarksFollowingCellsStale()
    {
        var vm = CreateVm();
        vm.NewNotebookCommand.Execute(null);
        vm.AddCellBelowCommand.Execute(vm.Cells[0]);
        vm.Cells[0].Status = QuantScriptCellStatus.Success;
        vm.Cells[1].Status = QuantScriptCellStatus.Success;

        vm.Cells[0].SourceCode += Environment.NewLine + "Print(\"updated\");";

        vm.Cells[0].Status.Should().Be(QuantScriptCellStatus.Stale);
        vm.Cells[1].Status.Should().Be(QuantScriptCellStatus.Stale);
    }

    [Fact]
    public async Task RunCellAndAdvanceCommand_AppendsNewCellAtNotebookEnd()
    {
        var vm = CreateVm(useRealRunner: true);
        vm.NewNotebookCommand.Execute(null);

        await vm.RunCellAndAdvanceCommand.ExecuteAsync(vm.Cells[0]);

        vm.Cells.Should().HaveCount(2);
        vm.SelectedCell.Should().Be(vm.Cells[1]);
        vm.Cells[0].Status.Should().Be(QuantScriptCellStatus.Success);
    }

    [Fact]
    public async Task RunCellCommand_ReplaysUsingPreviousCheckpoint()
    {
        var vm = CreateVm(useRealRunner: true);
        vm.NewNotebookCommand.Execute(null);
        vm.Cells[0].SourceCode = "var x = 2;";
        vm.AddCellBelowCommand.Execute(vm.Cells[0]);
        vm.Cells[1].SourceCode = "Print($\"x={x}\");";

        await vm.RunCellCommand.ExecuteAsync(vm.Cells[0]);
        await vm.RunCellCommand.ExecuteAsync(vm.Cells[1]);

        vm.Cells[1].Status.Should().Be(QuantScriptCellStatus.Success);
        vm.Cells[1].OutputText.Should().Contain("x=2");
        vm.Diagnostics.Should().Contain(entry => entry.Key == "Cell 2");
    }

    [Fact]
    public void ParameterValues_ArePreservedAcrossNotebookEdits()
    {
        var compiler = new ParameterAwareCompiler(
        [
            new ParameterDescriptor("lookback", "int", "Lookback", 20)
        ]);

        var vm = CreateVm(compiler: compiler);
        vm.NewNotebookCommand.Execute(null);
        vm.Parameters.Should().ContainSingle();

        vm.Parameters[0].RawValue = "55";
        vm.Cells[0].SourceCode += Environment.NewLine + "Print(\"rerun\");";

        vm.Parameters.Should().ContainSingle();
        vm.Parameters[0].RawValue.Should().Be("55");
        vm.ParameterSummaryText.Should().Be("1 parameters ready");
    }

    [Fact]
    public void InvalidParameters_BlockExecutionUntilFixed()
    {
        var compiler = new ParameterAwareCompiler(
        [
            new ParameterDescriptor("lookback", "int", "Lookback", 20)
        ]);

        var vm = CreateVm(compiler: compiler);
        vm.NewNotebookCommand.Execute(null);
        vm.Parameters[0].RawValue = "not-a-number";

        vm.HasInvalidParameters.Should().BeTrue();
        vm.CanRun.Should().BeFalse();
        vm.RunAllCommand.CanExecute(null).Should().BeFalse();
        vm.RunCellCommand.CanExecute(vm.Cells[0]).Should().BeFalse();
        vm.ParameterHintText.Should().Contain("before running");
        vm.RunReadinessText.Should().Contain("blocked");
    }

    [Fact]
    public async Task SaveNotebookCommand_TracksDraftAndSavedState()
    {
        var vm = CreateVm();
        vm.NewNotebookCommand.Execute(null);

        vm.DocumentStateText.Should().Be("Draft");
        vm.HasUnsavedChanges.Should().BeFalse();

        vm.Cells[0].SourceCode += Environment.NewLine + "Print(\"saved\");";

        vm.HasUnsavedChanges.Should().BeTrue();
        vm.DocumentStateText.Should().Be("Unsaved changes");

        await vm.SaveNotebookCommand.ExecuteAsync(null);

        vm.HasUnsavedChanges.Should().BeFalse();
        vm.DocumentStateText.Should().Be("Saved");
        vm.DocumentDisplayName.Should().EndWith(".mqnb");
    }

    [Fact]
    public async Task RunCellCommand_WhenPlotResultsArriveAsync_CreatesLegendBrushOnUiThread()
    {
        await RunOnStaThreadAsync(async () =>
        {
            var compiler = new RoslynScriptCompiler(
                Options.Create(new QuantScriptOptions()),
                NullLogger<RoslynScriptCompiler>.Instance);
            var runner = new BackgroundThreadScriptRunner(
                new ScriptRunner(
                    compiler,
                    new StubQuantDataContext(),
                    null!,
                    Options.Create(new QuantScriptOptions { RunTimeoutSeconds = 10 }),
                    NullLogger<ScriptRunner>.Instance));

            var vm = CreateVm(runner: runner);
            vm.NewNotebookCommand.Execute(null);
            vm.Cells[0].SourceCode = """
                var returns = new ReturnSeries("SPY", ReturnKind.Arithmetic, new[]
                {
                    new ReturnPoint(new DateOnly(2024, 1, 2), 0.01)
                });
                returns.Plot("Equity Curve");
                """;

            await vm.RunCellCommand.ExecuteAsync(vm.Cells[0]);

            vm.LegendEntries.Should().ContainSingle();
            var brush = vm.LegendEntries[0].SeriesColorBrush.Should().BeOfType<SolidColorBrush>().Subject;
            var accessBrush = () => _ = brush.Color;
            accessBrush.Should().NotThrow();
        });
    }

    private QuantScriptViewModel CreateVm(bool useRealRunner = false, IScriptRunner? runner = null, IQuantScriptCompiler? compiler = null)
    {
        Directory.CreateDirectory(_tempDirectory);

        compiler ??= new RoslynScriptCompiler(
            Options.Create(new QuantScriptOptions()),
            NullLogger<RoslynScriptCompiler>.Instance);

        runner ??= useRealRunner
            ? new ScriptRunner(
                compiler,
                new StubQuantDataContext(),
                null!,
                Options.Create(new QuantScriptOptions { RunTimeoutSeconds = 10 }),
                NullLogger<ScriptRunner>.Instance)
            : new Meridian.Wpf.Tests.Support.FakeScriptRunner();

        return new QuantScriptViewModel(
            runner,
            compiler,
            new NotebookExecutionSession(),
            new QuantScriptNotebookStore(new QuantScriptOptions
            {
                ScriptsDirectory = _tempDirectory,
                NotebookExtension = ".mqnb"
            }),
            new StubLayoutService(),
            Options.Create(new QuantScriptOptions
            {
                ScriptsDirectory = _tempDirectory,
                NotebookExtension = ".mqnb",
                NotebookCellWarningThreshold = 3
            }),
            NullLogger<QuantScriptViewModel>.Instance);
    }

    private static async Task RunOnStaThreadAsync(Func<Task> action)
    {
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));

            Task task;
            try
            {
                task = action();
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
                dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                return;
            }

            _ = task.ContinueWith(completedTask =>
            {
                if (completedTask.IsFaulted)
                    tcs.TrySetException(completedTask.Exception!.InnerExceptions);
                else if (completedTask.IsCanceled)
                    tcs.TrySetCanceled();
                else
                    tcs.TrySetResult(null);

                dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
            }, TaskScheduler.Default);

            Dispatcher.Run();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        try
        {
            await tcs.Task;
        }
        finally
        {
            thread.Join();
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory, true);
    }
}
#endif
