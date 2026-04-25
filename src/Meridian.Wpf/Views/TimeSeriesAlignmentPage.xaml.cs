using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Meridian.Ui.Services;
using Meridian.Wpf.Models;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

public partial class TimeSeriesAlignmentPage : Page
{
    private readonly TimeSeriesAlignmentService _alignmentService = TimeSeriesAlignmentService.Instance;
    private readonly ObservableCollection<string> _selectedSymbols = new();
    private readonly ObservableCollection<AlignmentHistoryEntry> _recentAlignments = new();

    public TimeSeriesAlignmentPage()
    {
        InitializeComponent();
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        SelectedSymbolsList.ItemsSource = _selectedSymbols;
        RecentAlignmentsList.ItemsSource = _recentAlignments;
        NoAlignmentsText.Visibility = _recentAlignments.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void AddSymbols_Click(object sender, RoutedEventArgs e)
    {
        var symbols = SymbolsBox.Text.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var symbol in symbols)
        {
            var upper = symbol.ToUpperInvariant();
            if (!_selectedSymbols.Contains(upper))
                _selectedSymbols.Add(upper);
        }
        SymbolsBox.Text = string.Empty;
    }

    private void RemoveSymbol_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string symbol)
            _selectedSymbols.Remove(symbol);
    }

    private void Preset_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PresetCombo is null ||
            FrequencyCombo is null ||
            GapHandlingCombo is null ||
            IncludeCloseCheck is null ||
            IncludeOpenCheck is null ||
            IncludeHighCheck is null ||
            IncludeLowCheck is null ||
            IncludeVolumeCheck is null)
        {
            return;
        }

        if (PresetCombo.SelectedItem is not ComboBoxItem item || item.Tag is not string preset)
            return;

        switch (preset)
        {
            case "cross-sectional":
                FrequencyCombo.SelectedIndex = 2; // 5 min
                GapHandlingCombo.SelectedIndex = 0; // ffill
                IncludeCloseCheck.IsChecked = true;
                IncludeOpenCheck.IsChecked = false;
                IncludeHighCheck.IsChecked = false;
                IncludeLowCheck.IsChecked = false;
                IncludeVolumeCheck.IsChecked = true;
                break;
            case "correlation":
                FrequencyCombo.SelectedIndex = 5; // daily
                GapHandlingCombo.SelectedIndex = 0; // ffill
                IncludeCloseCheck.IsChecked = true;
                IncludeOpenCheck.IsChecked = false;
                IncludeHighCheck.IsChecked = false;
                IncludeLowCheck.IsChecked = false;
                IncludeVolumeCheck.IsChecked = false;
                break;
            case "pairs":
                FrequencyCombo.SelectedIndex = 2; // 5 min
                GapHandlingCombo.SelectedIndex = 3; // drop
                IncludeCloseCheck.IsChecked = true;
                IncludeOpenCheck.IsChecked = true;
                IncludeHighCheck.IsChecked = true;
                IncludeLowCheck.IsChecked = true;
                IncludeVolumeCheck.IsChecked = true;
                break;
            case "rebalance":
                FrequencyCombo.SelectedIndex = 5; // daily
                GapHandlingCombo.SelectedIndex = 0; // ffill
                IncludeCloseCheck.IsChecked = true;
                IncludeOpenCheck.IsChecked = false;
                IncludeHighCheck.IsChecked = false;
                IncludeLowCheck.IsChecked = false;
                IncludeVolumeCheck.IsChecked = true;
                break;
        }
    }

    private async void RunAlignment_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedSymbols.Count < 2)
        {
            StatusText.Text = "Add at least 2 symbols for alignment.";
            return;
        }

        StatusText.Text = "Running alignment...";
        AlignmentProgress.Visibility = Visibility.Visible;
        AlignmentProgress.IsIndeterminate = true;
        ResultsCard.Visibility = Visibility.Collapsed;

        try
        {
            var frequencyTag = (FrequencyCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "5m";
            var gapTag = (GapHandlingCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "ffill";
            var exportTag = (ExportFormatCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Parquet";

            var interval = frequencyTag switch
            {
                "1s" => TimeSeriesInterval.Second1,
                "1m" => TimeSeriesInterval.Minute1,
                "5m" => TimeSeriesInterval.Minute5,
                "15m" => TimeSeriesInterval.Minute15,
                "1h" => TimeSeriesInterval.Hour1,
                "1d" => TimeSeriesInterval.Daily,
                _ => TimeSeriesInterval.Minute5
            };

            var gapStrategy = gapTag switch
            {
                "ffill" => GapStrategy.ForwardFill,
                "bfill" => GapStrategy.BackwardFill,
                "interpolate" => GapStrategy.LinearInterpolate,
                "drop" => GapStrategy.Skip,
                "zero" => GapStrategy.Zero,
                _ => GapStrategy.ForwardFill
            };

            _ = Enum.TryParse<ExportFormat>(exportTag, out var outputFormat);

            var alignmentName = string.IsNullOrWhiteSpace(AlignmentNameBox.Text)
                ? $"Alignment_{DateTime.Now:yyyyMMdd_HHmm}"
                : AlignmentNameBox.Text.Trim();

            var options = new AlignmentOptions
            {
                Symbols = _selectedSymbols.ToList(),
                Interval = interval,
                GapStrategy = gapStrategy,
                Timezone = (TimezoneCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "America/New_York",
                FromDate = FromDatePicker.SelectedDate is DateTime from ? DateOnly.FromDateTime(from) : null,
                ToDate = ToDatePicker.SelectedDate is DateTime to ? DateOnly.FromDateTime(to) : null,
                OutputFormat = outputFormat,
                OutputPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "Meridian", "Alignments", $"{alignmentName}.{exportTag.ToLower()}")
            };

            var result = await _alignmentService.AlignDataAsync(options);

            if (result.Success)
            {
                ResultRowsText.Text = $"{result.AlignedRecords:N0}";
                ResultSymbolsText.Text = $"{_selectedSymbols.Count}";
                ResultGapsText.Text = $"{result.GapsFilled:N0}";
                ResultFileSizeText.Text = result.OutputPath ?? "N/A";
                ResultOutputPathText.Text = $"Output: {result.OutputPath ?? "N/A"}";
                ResultsCard.Visibility = Visibility.Visible;
                StatusText.Text = $"Alignment complete in {result.Duration.TotalSeconds:F1}s.";

                _recentAlignments.Insert(0, new AlignmentHistoryEntry
                {
                    Name = alignmentName,
                    DetailsText = $"{_selectedSymbols.Count} symbols, {result.AlignedRecords:N0} rows",
                    DateText = DateTime.Now.ToString("MMM dd, yyyy HH:mm")
                });
                NoAlignmentsText.Visibility = Visibility.Collapsed;
            }
            else
            {
                StatusText.Text = $"Alignment failed: {result.Error}";
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
        finally
        {
            AlignmentProgress.IsIndeterminate = false;
            AlignmentProgress.Visibility = Visibility.Collapsed;
        }
    }

    private void SavePreset_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(AlignmentNameBox.Text))
        {
            StatusText.Text = "Enter an alignment name to save as preset.";
            return;
        }

        var existing = PresetCombo.Items.Cast<ComboBoxItem>().Any(i => i.Content?.ToString() == AlignmentNameBox.Text);
        if (!existing)
        {
            PresetCombo.Items.Insert(PresetCombo.Items.Count - 1, new ComboBoxItem { Content = AlignmentNameBox.Text, Tag = "custom" });
        }
        StatusText.Text = $"Preset \"{AlignmentNameBox.Text}\" saved.";
    }
}
