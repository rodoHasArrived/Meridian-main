using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Meridian.Ui.Services;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

public partial class PortfolioImportPage : Page
{
    private readonly PortfolioImportService _importService = PortfolioImportService.Instance;
    private readonly ObservableCollection<ImportHistoryEntry> _importHistory = new();

    public PortfolioImportPage()
    {
        InitializeComponent();
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        ImportHistoryList.ItemsSource = _importHistory;
        NoImportHistoryText.Visibility = _importHistory.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void BrowseFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "All supported|*.csv;*.txt;*.xlsx;*.json|CSV files|*.csv|Text files|*.txt|Excel files|*.xlsx|JSON files|*.json",
            Title = "Select Portfolio File"
        };
        if (dialog.ShowDialog() == true)
            FilePathBox.Text = dialog.FileName;
    }

    private async void ImportFile_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(FilePathBox.Text))
        {
            ImportStatusText.Text = "Please select a file first.";
            return;
        }

        ImportStatusText.Text = "Importing...";
        try
        {
            var format = (FileFormatCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "csv";
            PortfolioParseResult parseResult;

            if (format == "excel" || FilePathBox.Text.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                parseResult = await _importService.ParseJsonAsync(FilePathBox.Text);
            }
            else
            {
                parseResult = await _importService.ParseCsvAsync(FilePathBox.Text);
            }

            if (!parseResult.Success || parseResult.Entries.Count == 0)
            {
                ImportStatusText.Text = $"Parse error: {parseResult.Error ?? "No symbols found"}";
                return;
            }

            var importResult = await _importService.ImportAsSubscriptionsAsync(
                parseResult.Entries,
                enableTrades: true,
                enableDepth: false);

            ImportStatusText.Text = importResult.Success
                ? $"Imported {importResult.ImportedCount} symbols ({importResult.SkippedCount} skipped)"
                : $"Error: {importResult.Error ?? string.Join(", ", importResult.Errors)}";

            if (importResult.Success)
                AddToHistory($"File: {System.IO.Path.GetFileName(FilePathBox.Text)}", importResult.ImportedCount);
        }
        catch (Exception ex) { ImportStatusText.Text = $"Error: {ex.Message}"; }
    }

    private async void ImportIndex_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string indexId) return;

        IndexImportStatusText.Text = $"Importing {btn.Content}...";
        btn.IsEnabled = false;
        try
        {
            var constituents = await _importService.GetIndexConstituentsAsync(indexId);
            if (!constituents.Success || constituents.Symbols.Count == 0)
            {
                IndexImportStatusText.Text = $"Error: {constituents.Error ?? "No constituents found"}";
                return;
            }

            var entries = constituents.Symbols.Select(s => new PortfolioEntry { Symbol = s }).ToList();
            var importResult = await _importService.ImportAsSubscriptionsAsync(entries);

            IndexImportStatusText.Text = importResult.Success
                ? $"Added {importResult.ImportedCount} symbols from {constituents.IndexName}"
                : $"Error: {importResult.Error ?? string.Join(", ", importResult.Errors)}";

            if (importResult.Success) AddToHistory($"Index: {constituents.IndexName}", importResult.ImportedCount);
        }
        catch (Exception ex) { IndexImportStatusText.Text = $"Error: {ex.Message}"; }
        finally { btn.IsEnabled = true; }
    }

    private async void AddManualSymbols_Click(object sender, RoutedEventArgs e)
    {
        var text = ManualSymbolsBox.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            ManualStatusText.Text = "Enter symbols first.";
            return;
        }

        var symbols = text.Split(new[] { ',', ' ', '\n', '\r', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToUpperInvariant())
            .Distinct()
            .ToList();

        ManualStatusText.Text = "Adding...";
        try
        {
            var entries = symbols.Select(s => new PortfolioEntry { Symbol = s }).ToList();
            var result = await _importService.ImportAsSubscriptionsAsync(entries);

            ManualStatusText.Text = result.Success
                ? $"Added {result.ImportedCount} symbols ({result.SkippedCount} skipped)"
                : $"Error: {result.Error ?? string.Join(", ", result.Errors)}";

            if (result.Success)
            {
                ManualSymbolsBox.Text = string.Empty;
                AddToHistory("Manual entry", result.ImportedCount);
            }
        }
        catch (Exception ex) { ManualStatusText.Text = $"Error: {ex.Message}"; }
    }

    private void AddToHistory(string source, int count)
    {
        _importHistory.Insert(0, new ImportHistoryEntry
        {
            Source = source,
            CountText = $"{count} symbols",
            DateText = DateTime.Now.ToString("MMM dd, yyyy HH:mm")
        });
        NoImportHistoryText.Visibility = Visibility.Collapsed;
    }
}

public sealed class ImportHistoryEntry
{
    public string Source { get; init; } = string.Empty;
    public string CountText { get; init; } = string.Empty;
    public string DateText { get; init; } = string.Empty;
}
