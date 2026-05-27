using System.Collections.ObjectModel;
using Meridian.QuantScript.Documents;
using Meridian.QuantScript.Plotting;
using Meridian.Ui.Services.Collections;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.ViewModels;

internal sealed class QuantScriptCollectionsSectionViewModel
{
    public ObservableCollection<ScriptDocumentEntry> Documents { get; } = [];
    public ObservableCollection<QuantScriptTemplateDefinition> Templates { get; } = [];
    public ObservableCollection<NotebookCellViewModel> NotebookCells { get; } = [];
    public ObservableCollection<ParameterViewModel> Parameters { get; } = [];
    public BoundedObservableCollection<ConsoleEntry> ConsoleOutput { get; } = new(10_000);
    public ObservableCollection<PlotViewModel> Charts { get; } = [];
    public ObservableCollection<MetricEntry> Metrics { get; } = [];
    public ObservableCollection<TradeEntry> Trades { get; } = [];
    public ObservableCollection<DiagnosticEntry> Diagnostics { get; } = [];
    public ObservableCollection<QuantScriptExecutionRecord> RunHistory { get; } = [];
    public ObservableCollection<ChartLegendEntry> LegendEntries { get; } = [];
}
