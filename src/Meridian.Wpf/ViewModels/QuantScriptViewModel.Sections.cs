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

internal sealed class QuantScriptSelectionSectionViewModel : BindableBase
{
    private ScriptDocumentEntry? _selectedDocument;
    private NotebookCellViewModel? _selectedCell;
    private QuantScriptTemplateDefinition? _selectedTemplate;
    private QuantScriptExecutionRecord? _selectedExecutionRecord;

    public ScriptDocumentEntry? SelectedDocument { get => _selectedDocument; set => SetProperty(ref _selectedDocument, value); }
    public NotebookCellViewModel? SelectedCell { get => _selectedCell; set => SetProperty(ref _selectedCell, value); }
    public QuantScriptTemplateDefinition? SelectedTemplate { get => _selectedTemplate; set => SetProperty(ref _selectedTemplate, value); }
    public QuantScriptExecutionRecord? SelectedExecutionRecord { get => _selectedExecutionRecord; set => SetProperty(ref _selectedExecutionRecord, value); }
}

internal sealed class QuantScriptRunStateSectionViewModel : BindableBase
{
    private bool _isRunning;
    private double _progressFraction;
    private string _statusText = "Ready";
    private string _elapsedText = "--";
    private string _memoryText = "--";
    private int _activeResultsTab;

    public bool IsRunning { get => _isRunning; set => SetProperty(ref _isRunning, value); }
    public double ProgressFraction { get => _progressFraction; set => SetProperty(ref _progressFraction, value); }
    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }
    public string ElapsedText { get => _elapsedText; set => SetProperty(ref _elapsedText, value); }
    public string MemoryText { get => _memoryText; set => SetProperty(ref _memoryText, value); }
    public int ActiveResultsTab { get => _activeResultsTab; set => SetProperty(ref _activeResultsTab, value); }
}
