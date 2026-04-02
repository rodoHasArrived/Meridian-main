using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
    private bool _isIndexImporting;

    public PortfolioImportViewModel()
    {
        _importService = PortfolioImportService.Instance;
        ImportHistory = new ObservableCollection<ImportHistoryEntry>();
    }

    public ObservableCollection<ImportHistoryEntry> ImportHistory { get; }

    public bool HasImportHistory => ImportHistory.Count > 0;

    public string FilePath
    {
        get => _filePath;
        set => SetProperty(ref _filePath, value);
    }

    public string SelectedFileFormat
    {
        get => _selectedFileFormat;
        set => SetProperty(ref _selectedFileFormat, value);
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
        set => SetProperty(ref _manualSymbols, value);
    }

    public string ManualImportStatus
    {
        get => _manualImportStatus;
        set => SetProperty(ref _manualImportStatus, value);
    }

    public bool IsIndexImporting
    {
        get => _isIndexImporting;
        private set => SetProperty(ref _isIndexImporting, value);
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
    }

    public async Task ImportIndexAsync(string indexId, string displayName)
    {
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

        var symbols = ManualSymbols
            .Split(new[] { ',', ' ', '\n', '\r', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToUpperInvariant())
            .Distinct()
            .ToList();

        ManualImportStatus = "Adding...";
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
    }

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
}

public sealed class ImportHistoryEntry
{
    public string Source { get; init; } = string.Empty;
    public string CountText { get; init; } = string.Empty;
    public string DateText { get; init; } = string.Empty;
}
