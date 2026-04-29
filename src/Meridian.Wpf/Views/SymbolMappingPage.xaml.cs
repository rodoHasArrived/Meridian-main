using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.ViewModels;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Views;

/// <summary>
/// Symbol mapping page for managing provider-specific symbol translations,
/// testing mappings, and importing/exporting CSV mapping files.
/// </summary>
public partial class SymbolMappingPage : Page
{
    private readonly WpfServices.LoggingService _loggingService;
    private readonly SymbolMappingViewModel _viewModel;

    public SymbolMappingPage()
    {
        InitializeComponent();

        _loggingService = WpfServices.LoggingService.Instance;
        _viewModel = new SymbolMappingViewModel();
        DataContext = _viewModel;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.LoadAsync();
    }

    private async void ImportCsv_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            Title = "Import Symbol Mappings"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var csv = await File.ReadAllTextAsync(dialog.FileName);
            var imported = await _viewModel.ImportCsvContentAsync(csv);
            MessageBox.Show(
                $"Imported {imported} symbol mapping{(imported == 1 ? "" : "s")}.",
                "Import Complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to import CSV", ex);
            MessageBox.Show($"Import failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv",
            Title = "Export Symbol Mappings",
            FileName = "symbol-mappings.csv"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var csv = _viewModel.ExportCsv();
            File.WriteAllText(dialog.FileName, csv);
            MessageBox.Show("Mappings exported successfully.", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to export CSV", ex);
            MessageBox.Show($"Export failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
