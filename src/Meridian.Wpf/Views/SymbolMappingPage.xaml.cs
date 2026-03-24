using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Meridian.Ui.Services;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Views;

/// <summary>
/// Symbol mapping page for managing provider-specific symbol translations,
/// testing mappings, and importing/exporting CSV mapping files.
/// </summary>
public partial class SymbolMappingPage : Page
{
    private readonly SymbolMappingService _mappingService;
    private readonly WpfServices.LoggingService _loggingService;
    private readonly ObservableCollection<ProviderInfoItem> _providers = new();
    private readonly ObservableCollection<MappingItem> _mappings = new();
    private readonly ObservableCollection<TestResultItem> _testResults = new();

    public SymbolMappingPage()
    {
        InitializeComponent();

        _mappingService = SymbolMappingService.Instance;
        _loggingService = WpfServices.LoggingService.Instance;

        ProvidersList.ItemsSource = _providers;
        MappingsList.ItemsSource = _mappings;
        TestResultsList.ItemsSource = _testResults;

        // Populate provider combo
        foreach (var provider in SymbolMappingService.KnownProviders)
        {
            NewProviderCombo.Items.Add(new ComboBoxItem { Content = $"{provider.Id} - {provider.DisplayName}", Tag = provider.Id });
        }
        if (NewProviderCombo.Items.Count > 0)
            NewProviderCombo.SelectedIndex = 0;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        await _mappingService.LoadAsync();
        LoadProviders();
        LoadMappings();
    }

    private void LoadProviders()
    {
        _providers.Clear();
        foreach (var provider in SymbolMappingService.KnownProviders)
        {
            _providers.Add(new ProviderInfoItem
            {
                Id = provider.Id,
                DisplayName = provider.DisplayName,
                Description = provider.TransformDescription
            });
        }
    }

    private void LoadMappings()
    {
        _mappings.Clear();
        var mappings = _mappingService.GetMappings();

        foreach (var mapping in mappings)
        {
            var providerTexts = new List<string>();
            if (mapping.ProviderSymbols != null)
            {
                foreach (var (providerId, symbol) in mapping.ProviderSymbols)
                {
                    if (!string.IsNullOrWhiteSpace(symbol))
                    {
                        providerTexts.Add($"{providerId}: {symbol}");
                    }
                }
            }

            _mappings.Add(new MappingItem
            {
                CanonicalSymbol = mapping.CanonicalSymbol,
                ProviderMappingsText = providerTexts.Count > 0
                    ? string.Join(", ", providerTexts)
                    : "(defaults only)",
                UpdatedText = mapping.UpdatedAt.ToString("MMM dd, yyyy")
            });
        }

        MappingCountText.Text = $"{_mappings.Count} mapping{(_mappings.Count == 1 ? "" : "s")}";
        NoMappingsPanel.Visibility = _mappings.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        MappingsList.Visibility = _mappings.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void TestMapping_Click(object sender, RoutedEventArgs e)
    {
        var symbol = TestSymbolBox.Text?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(symbol))
        {
            MessageBox.Show("Please enter a symbol to test.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _testResults.Clear();
        var results = _mappingService.TestMapping(symbol);

        foreach (var (providerId, mappedSymbol) in results)
        {
            var provider = SymbolMappingService.KnownProviders.FirstOrDefault(p => p.Id == providerId);
            _testResults.Add(new TestResultItem
            {
                ProviderName = provider?.DisplayName ?? providerId,
                MappedSymbol = mappedSymbol
            });
        }

        TestResultsList.Visibility = Visibility.Visible;
    }

    private async void AddMapping_Click(object sender, RoutedEventArgs e)
    {
        var canonical = NewCanonicalBox.Text?.Trim().ToUpperInvariant();
        var providerSymbol = NewProviderSymbolBox.Text?.Trim();

        if (string.IsNullOrWhiteSpace(canonical) || string.IsNullOrWhiteSpace(providerSymbol))
        {
            MessageBox.Show("Please enter both a canonical symbol and provider symbol.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var selectedItem = NewProviderCombo.SelectedItem as ComboBoxItem;
        var providerId = selectedItem?.Tag as string ?? "Alpaca";

        try
        {
            var mapping = _mappingService.GetMapping(canonical) ?? new SymbolMapping { CanonicalSymbol = canonical };
            mapping.ProviderSymbols ??= new Dictionary<string, string>();
            mapping.ProviderSymbols[providerId] = providerSymbol;
            mapping.IsCustomMapping = true;

            await _mappingService.AddOrUpdateMappingAsync(mapping);

            NewCanonicalBox.Text = "";
            NewProviderSymbolBox.Text = "";
            LoadMappings();
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to add mapping", ex);
            MessageBox.Show($"Failed to add mapping: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void RemoveMapping_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string canonical)
        {
            var confirm = MessageBox.Show(
                $"Remove mapping for {canonical}?",
                "Confirm Remove",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm == MessageBoxResult.Yes)
            {
                try
                {
                    await _mappingService.RemoveMappingAsync(canonical);
                    LoadMappings();
                }
                catch (Exception ex)
                {
                    _loggingService.LogError("Failed to remove mapping", ex);
                }
            }
        }
    }

    private async void ImportCsv_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            Title = "Import Symbol Mappings"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var csv = await System.IO.File.ReadAllTextAsync(dialog.FileName);
                var imported = await _mappingService.ImportFromCsvAsync(csv);
                MessageBox.Show($"Imported {imported} symbol mappings.", "Import Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                LoadMappings();
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Failed to import CSV", ex);
                MessageBox.Show($"Import failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var csv = _mappingService.ExportToCsv();
                System.IO.File.WriteAllText(dialog.FileName, csv);
                MessageBox.Show("Mappings exported successfully.", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Failed to export CSV", ex);
                MessageBox.Show($"Export failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    public sealed class ProviderInfoItem
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    public sealed class MappingItem
    {
        public string CanonicalSymbol { get; set; } = string.Empty;
        public string ProviderMappingsText { get; set; } = string.Empty;
        public string UpdatedText { get; set; } = string.Empty;
    }

    public sealed class TestResultItem
    {
        public string ProviderName { get; set; } = string.Empty;
        public string MappedSymbol { get; set; } = string.Empty;
    }
}
