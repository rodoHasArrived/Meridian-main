using System.Collections.ObjectModel;
using Meridian.QuantScript.Documents;
using Meridian.QuantScript.Plotting;
using Meridian.Ui.Services.Collections;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.ViewModels;

internal sealed class QuantScriptCollectionsSectionViewModel
{
    public QuantScriptCollectionsSectionViewModel(QuantScriptCollectionRefs refs)
    {
        Documents = refs.Documents;
        Templates = refs.Templates;
        NotebookCells = refs.NotebookCells;
        Parameters = refs.Parameters;
        ConsoleOutput = refs.ConsoleOutput;
        Charts = refs.Charts;
        Metrics = refs.Metrics;
        Trades = refs.Trades;
        Diagnostics = refs.Diagnostics;
        RunHistory = refs.RunHistory;
        LegendEntries = refs.LegendEntries;
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

    internal sealed record QuantScriptCollectionRefs(
        ObservableCollection<ScriptDocumentEntry> Documents,
        ObservableCollection<QuantScriptTemplateDefinition> Templates,
        ObservableCollection<NotebookCellViewModel> NotebookCells,
        ObservableCollection<ParameterViewModel> Parameters,
        BoundedObservableCollection<ConsoleEntry> ConsoleOutput,
        ObservableCollection<PlotViewModel> Charts,
        ObservableCollection<MetricEntry> Metrics,
        ObservableCollection<TradeEntry> Trades,
        ObservableCollection<DiagnosticEntry> Diagnostics,
        ObservableCollection<QuantScriptExecutionRecord> RunHistory,
        ObservableCollection<ChartLegendEntry> LegendEntries)
    {
        public static QuantScriptCollectionRefs FromOwner(QuantScriptViewModel owner)
            => new(
                owner.Documents,
                owner.Templates,
                owner.NotebookCells,
                owner.Parameters,
                owner.ConsoleOutput,
                owner.Charts,
                owner.Metrics,
                owner.Trades,
                owner.Diagnostics,
                owner.RunHistory,
                owner.LegendEntries);
    }
}
