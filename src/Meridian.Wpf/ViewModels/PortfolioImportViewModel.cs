using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Meridian.Ui.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// ViewModel for the Portfolio Import page. Exposes observable state for all import
/// operations (file, index, and manual entry) and delegates work to <see cref="PortfolioImportService"/>.
/// </summary>
public sealed class PortfolioImportViewModel : BindableBase
{
    private readonly PortfolioImportService _importService;

    private string _filePath = string.Empty;
    private string _selectedFileFormat = "csv";
    private string _importStatus = string.Empty;
    private string _indexImportStatus = string.Empty;
    private string _manualSymbols = string.Empty;
    private string _manualImportStatus = string.Empty;
    private bool _isFileImporting;
    private bool _isIndexImporting;
    private bool _isManualImporting;

    public PortfolioImportViewModel()
    {
        _importService = PortfolioImportService.Instance;
        ImportHistory = new ObservableCollection<ImportHistoryEntry>();
        BrowseFileCommand = new RelayCommand(BrowseFile);
        ImportFileCommand = new AsyncRelayCommand(ImportFileAsync, () => CanImportFile);
        ImportIndexCommand = new AsyncRelayCommand<string>(ImportIndexAsync, CanImportIndex);
        AddManualSymbolsCommand = new AsyncRelayCommand(AddManualSymbolsAsync, () => CanAddManualSymbols);
    }

    public ObservableCollection<ImportHistoryEntry> ImportHistory { get; }

    public bool HasImportHistory => ImportHistory.Count > 0;

    public IRelayCommand BrowseFileCommand { get; }

    public IAsyncRelayCommand ImportFileCommand { get; }

    public IAsyncRelayCommand<string> ImportIndexCommand { get; }

    public IAsyncRelayCommand AddManualSymbolsCommand { get; }

    public string FilePath
    {
        get => _filePath;
        set
        {
            if (SetProperty(ref _filePath, value))
            {
                RaiseFileImportPresentationChanged();
            }
        }
    }

    public string SelectedFileFormat
    {
        get => _selectedFileFormat;
        set
        {
            if (SetProperty(ref _selectedFileFormat, value))
            {
                RaiseFileImportPresentationChanged();
            }
        }
    }

    public string ImportStatus
    {
        get => _importStatus;
        set => SetProperty(ref _importStatus, value);
    }

    public string IndexImportStatus
    {
        get => _indexImportStatus;
        set => SetProperty(ref _indexImportStatus, value);
    }

    public string ManualSymbols
    {
        get => _manualSymbols;
        set
        {
            if (SetProperty(ref _manualSymbols, value))
            {
                RaiseManualImportPresentationChanged();
            }
        }
    }

    public string ManualImportStatus
    {
        get => _manualImportStatus;
        set => SetProperty(ref _manualImportStatus, value);
    }

    public bool IsFileImporting
    {
        get => _isFileImporting;
        private set
        {
            if (SetProperty(ref _isFileImporting, value))
            {
                RaiseFileImportPresentationChanged();
            }
        }
    }

    public bool IsIndexImporting
    {
        get => _isIndexImporting;
        private set
        {
            if (SetProperty(ref _isIndexImporting, value))
            {
                ImportIndexCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsManualImporting
    {
        get => _isManualImporting;
        private set
        {
            if (SetProperty(ref _isManualImporting, value))
            {
                RaiseManualImportPresentationChanged();
            }
        }
    }

    public bool CanImportFile => !IsFileImporting && !string.IsNullOrWhiteSpace(FilePath);

    public string FileImportGuidanceTitle
    {
        get
        {
            if (IsFileImporting)
            {
                return "Import in progress";
            }

            return CanImportFile ? "File ready to import" : "Select an import file";
        }
    }

    public string FileImportGuidanceText
    {
        get
        {
            if (IsFileImporting)
            {
                return "Parsing the selected file and adding symbols to monitored subscriptions.";
            }

            if (!CanImportFile)
            {
                return "Choose a CSV, text, Excel, or JSON portfolio file before starting the import.";
            }

            var fileName = Path.GetFileName(FilePath);
            return $"Ready to import {fileName} as {FormatFileFormat(SelectedFileFormat)}.";
        }
    }

    public int ManualSymbolCount => ExtractManualSymbols(ManualSymbols).Count;

    public bool CanAddManualSymbols => !IsManualImporting && ManualSymbolCount > 0;

    public string ManualSymbolCountText => ManualSymbolCount switch
    {
        0 => "No symbols queued",
        1 => "1 unique symbol queued",
        var count => $"{count} unique symbols queued"
    };

    public string ManualEntryGuidanceText
    {
        get
        {
            if (IsManualImporting)
            {
                return "Adding the queued symbols to monitored subscriptions.";
            }

            return CanAddManualSymbols
                ? "Ready to add the queued symbols without retyping duplicates."
                : "Paste one or more symbols to enable the add action.";
        }
    }

    public void BrowseFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "All supported|*.csv;*.txt;*.xlsx;*.json|CSV files|*.csv|Text files|*.txt|Excel files|*.xlsx|JSON files|*.json",
            Title = "Select Portfolio File"
        };
        if (dialog.ShowDialog() == true)
        {
            FilePath = dialog.FileName;
        }
    }

    public async Task ImportFileAsync()
    {
        if (string.IsNullOrWhiteSpace(FilePath))
        {
            ImportStatus = "Please select a file first.";
            return;
        }

        ImportStatus = "Importing...";
        IsFileImporting = true;
        try
        {
            PortfolioParseResult parseResult;

            if (SelectedFileFormat == "excel" || FilePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                parseResult = await _importService.ParseJsonAsync(FilePath);
            }
            else
            {
                parseResult = await _importService.ParseCsvAsync(FilePath);
            }

            if (!parseResult.Success || parseResult.Entries.Count == 0)
            {
                ImportStatus = $"Parse error: {parseResult.Error ?? "No symbols found"}";
                return;
            }

            var importResult = await _importService.ImportAsSubscriptionsAsync(
                parseResult.Entries,
                enableTrades: true,
                enableDepth: false);

            ImportStatus = importResult.Success
                ? $"Imported {importResult.ImportedCount} symbols ({importResult.SkippedCount} skipped)"
                : $"Error: {importResult.Error ?? string.Join(", ", importResult.Errors)}";

            if (importResult.Success)
            {
                AddToHistory($"File: {Path.GetFileName(FilePath)}", importResult.ImportedCount);
            }
        }
        catch (Exception ex)
        {
            ImportStatus = $"Error: {ex.Message}";
        }
        finally
        {
            IsFileImporting = false;
        }
    }

    public async Task ImportIndexAsync(string? indexId)
    {
        if (string.IsNullOrWhiteSpace(indexId))
        {
            return;
        }

        var displayName = GetIndexDisplayName(indexId);
        IndexImportStatus = $"Importing {displayName}...";
        IsIndexImporting = true;
        try
        {
            var constituents = await _importService.GetIndexConstituentsAsync(indexId);
            if (!constituents.Success || constituents.Symbols.Count == 0)
            {
                IndexImportStatus = $"Error: {constituents.Error ?? "No constituents found"}";
                return;
            }

            var entries = constituents.Symbols.Select(s => new PortfolioEntry { Symbol = s }).ToList();
            var importResult = await _importService.ImportAsSubscriptionsAsync(entries);

            IndexImportStatus = importResult.Success
                ? $"Added {importResult.ImportedCount} symbols from {constituents.IndexName}"
                : $"Error: {importResult.Error ?? string.Join(", ", importResult.Errors)}";

            if (importResult.Success)
            {
                AddToHistory($"Index: {constituents.IndexName}", importResult.ImportedCount);
            }
        }
        catch (Exception ex)
        {
            IndexImportStatus = $"Error: {ex.Message}";
        }
        finally
        {
            IsIndexImporting = false;
        }
    }

    public async Task AddManualSymbolsAsync()
    {
        if (string.IsNullOrWhiteSpace(ManualSymbols))
        {
            ManualImportStatus = "Enter symbols first.";
            return;
        }

        var symbols = ExtractManualSymbols(ManualSymbols);
        if (symbols.Count == 0)
        {
            ManualImportStatus = "Enter symbols first.";
            return;
        }

        ManualImportStatus = "Adding...";
        IsManualImporting = true;
        try
        {
            var entries = symbols.Select(s => new PortfolioEntry { Symbol = s }).ToList();
            var result = await _importService.ImportAsSubscriptionsAsync(entries);

            ManualImportStatus = result.Success
                ? $"Added {result.ImportedCount} symbols ({result.SkippedCount} skipped)"
                : $"Error: {result.Error ?? string.Join(", ", result.Errors)}";

            if (result.Success)
            {
                ManualSymbols = string.Empty;
                AddToHistory("Manual entry", result.ImportedCount);
            }
        }
        catch (Exception ex)
        {
            ManualImportStatus = $"Error: {ex.Message}";
        }
        finally
        {
            IsManualImporting = false;
        }
    }

    public static IReadOnlyList<string> ExtractManualSymbols(string manualSymbols) =>
        manualSymbols
            .Split(new[] { ',', ' ', '\n', '\r', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToUpperInvariant())
            .Distinct()
            .ToList();

    private void AddToHistory(string source, int count)
    {
        ImportHistory.Insert(0, new ImportHistoryEntry
        {
            Source = source,
            CountText = $"{count} symbols",
            DateText = DateTime.Now.ToString("MMM dd, yyyy HH:mm")
        });
        RaisePropertyChanged(nameof(HasImportHistory));
    }

    private static string FormatFileFormat(string fileFormat) => fileFormat switch
    {
        "csv-header" => "CSV with headers",
        "text" => "plain text",
        "excel" => "Excel",
        _ => "CSV"
    };

    private static string GetIndexDisplayName(string indexId) => indexId switch
    {
        "sp500" => "S&P 500",
        "nasdaq100" => "NASDAQ-100",
        "djia" => "Dow Jones 30",
        "russell2000" => "Russell 2000",
        "sp400" => "S&P MidCap 400",
        _ => indexId
    };

    private bool CanImportIndex(string? indexId) => !IsIndexImporting && !string.IsNullOrWhiteSpace(indexId);

    private void RaiseFileImportPresentationChanged()
    {
        RaisePropertyChanged(nameof(CanImportFile));
        RaisePropertyChanged(nameof(FileImportGuidanceTitle));
        RaisePropertyChanged(nameof(FileImportGuidanceText));
        ImportFileCommand.NotifyCanExecuteChanged();
    }

    private void RaiseManualImportPresentationChanged()
    {
        RaisePropertyChanged(nameof(ManualSymbolCount));
        RaisePropertyChanged(nameof(CanAddManualSymbols));
        RaisePropertyChanged(nameof(ManualSymbolCountText));
        RaisePropertyChanged(nameof(ManualEntryGuidanceText));
        AddManualSymbolsCommand.NotifyCanExecuteChanged();
    }
}

public sealed class ImportHistoryEntry
{
    public string Source { get; init; } = string.Empty;
    public string CountText { get; init; } = string.Empty;
    public string DateText { get; init; } = string.Empty;
}
