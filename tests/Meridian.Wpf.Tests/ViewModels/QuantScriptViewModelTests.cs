#if WINDOWS
using System.IO;
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

    private QuantScriptViewModel CreateVm(bool useRealRunner = false)
    {
        Directory.CreateDirectory(_tempDirectory);

        var compiler = new RoslynScriptCompiler(
            Options.Create(new QuantScriptOptions()),
            NullLogger<RoslynScriptCompiler>.Instance);

        IScriptRunner runner = useRealRunner
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

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory, true);
    }
}
#endif
