using System.Collections.ObjectModel;
using Meridian.QuantScript.Documents;
using Meridian.QuantScript.Plotting;
using Meridian.Ui.Services.Collections;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.ViewModels;

public sealed class QuantScriptCollectionsSectionViewModel
{
    public QuantScriptCollectionsSectionViewModel(
        ObservableCollection<ScriptDocumentEntry> documents,
        ObservableCollection<QuantScriptTemplateDefinition> templates,
        ObservableCollection<NotebookCellViewModel> notebookCells,
        ObservableCollection<ParameterViewModel> parameters,
        BoundedObservableCollection<ConsoleEntry> consoleOutput,
        ObservableCollection<PlotViewModel> charts,
        ObservableCollection<MetricEntry> metrics,
        ObservableCollection<TradeEntry> trades,
        ObservableCollection<DiagnosticEntry> diagnostics,
        ObservableCollection<QuantScriptExecutionRecord> runHistory,
        ObservableCollection<ChartLegendEntry> legendEntries)
    {
        Documents = documents;
        Templates = templates;
        NotebookCells = notebookCells;
        Parameters = parameters;
        ConsoleOutput = consoleOutput;
        Charts = charts;
        Metrics = metrics;
        Trades = trades;
        Diagnostics = diagnostics;
        RunHistory = runHistory;
        LegendEntries = legendEntries;
    }

    public ObservableCollection<ScriptDocumentEntry> Documents { get; }
    public ObservableCollection<QuantScriptTemplateDefinition> Templates { get; }
    public ObservableCollection<NotebookCellViewModel> NotebookCells { get; }
    public ObservableCollection<ParameterViewModel> Parameters { get; }
    public BoundedObservableCollection<ConsoleEntry> ConsoleOutput { get; }
    public ObservableCollection<PlotViewModel> Charts { get; }
    public ObservableCollection<MetricEntry> Metrics { get; }
    public ObservableCollection<TradeEntry> Trades { get; }
    public ObservableCollection<DiagnosticEntry> Diagnostics { get; }
    public ObservableCollection<QuantScriptExecutionRecord> RunHistory { get; }
    public ObservableCollection<ChartLegendEntry> LegendEntries { get; }
}
