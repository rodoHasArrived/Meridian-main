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
    private CancellationTokenSource? _lifetime;

    public SymbolMappingPage()
        : this(new SymbolMappingViewModel())
    {
    }

    public SymbolMappingPage(SymbolMappingViewModel viewModel)
    {
        InitializeComponent();

        _loggingService = WpfServices.LoggingService.Instance;
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = _viewModel;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        _lifetime ??= new CancellationTokenSource();
        try
        {
            await _viewModel.LoadAsync(_lifetime.Token);
        }
        catch (System.OperationCanceledException)
        {
            // Navigation cancelled the in-flight load before it completed; benign during teardown.
            global::Meridian.Wpf.Services.LoggingService.Instance.LogDebug(
                "Page load cancelled during navigation.",
                ("page", GetType().Name));
        }
        catch (System.Exception ex)
        {
            global::Meridian.Wpf.Services.LoggingService.Instance.LogError("Symbol Mapping page failed to load.", ex);
        }
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        _lifetime?.Cancel();
        _lifetime?.Dispose();
        _lifetime = null;
        _viewModel.Stop();
    }

    private async void ImportCsv_Click(object sender, RoutedEventArgs e)
        => await global::Meridian.Wpf.Services.AsyncHandlerGuard.RunAsync(() => ImportCsv_ClickCoreAsync(sender, e), nameof(ImportCsv_Click)).ConfigureAwait(true);

    private async System.Threading.Tasks.Task ImportCsv_ClickCoreAsync(object sender, RoutedEventArgs e)
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
            var ct = _lifetime?.Token ?? CancellationToken.None;
            var csv = await File.ReadAllTextAsync(dialog.FileName, ct);
            var imported = await _viewModel.ImportCsvContentAsync(csv, ct);
            MessageBox.Show(
                $"Imported {imported} symbol mapping{(imported == 1 ? "" : "s")}.",
                "Import Complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _loggingService.LogError("Failed to import CSV", ex);
            MessageBox.Show($"Import failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ExportCsv_Click(object sender, RoutedEventArgs e)
        => await WpfServices.AsyncHandlerGuard.RunAsync(ExportCsvAsync, nameof(ExportCsv_Click)).ConfigureAwait(true);

    private async Task ExportCsvAsync()
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
            var ct = _lifetime?.Token ?? CancellationToken.None;
            var csv = await _viewModel.ExportCsvAsync(ct);
            await File.WriteAllTextAsync(dialog.FileName, csv, ct);
            MessageBox.Show("Mappings exported successfully.", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _loggingService.LogError("Failed to export CSV", ex);
            MessageBox.Show($"Export failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
