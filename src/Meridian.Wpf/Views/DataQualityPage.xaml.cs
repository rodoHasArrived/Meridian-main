using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Meridian.Ui.Services.DataQuality;
using Meridian.Wpf.Models;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

public partial class DataQualityPage : Page
{
    private readonly DataQualityViewModel _viewModel;

    public DataQualityPage(DataQualityViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;

        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
        SizeChanged += (_, _) => RenderTrendChart(_viewModel.TrendPoints);
        _viewModel.TrendChartChanged += (_, _) => RenderTrendChart(_viewModel.TrendPoints);
        _viewModel.DrilldownChanged += (_, _) => ApplyDrilldownHeatmap();
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.StartAsync();
        RenderTrendChart(_viewModel.TrendPoints);
        ApplyDrilldownHeatmap();
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e) => _viewModel.Stop();

    private void TimeWindow_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (TimeWindowCombo.SelectedItem is ComboBoxItem item && item.Tag is string window)
        {
            _viewModel.SetTimeRange(window);
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await _viewModel.RefreshAsync();

    private async void RunQualityCheck_Click(object sender, RoutedEventArgs e)
    {
        var path = PromptForQualityCheckPath();
        if (!string.IsNullOrWhiteSpace(path))
        {
            await _viewModel.RunQualityCheckAsync(path);
        }
    }

    private async void RepairGap_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string gapId && ShowConfirmation("Repair Gap", "Start repair for this gap?"))
        {
            await _viewModel.RepairGapAsync(gapId);
        }
    }

    private async void RepairAllGaps_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.Gaps.Count > 0 && ShowConfirmation("Repair All Gaps", "Start repair for all listed gaps?"))
        {
            await _viewModel.RepairAllGapsAsync();
        }
    }

    private async void CompareProviders_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string symbol)
        {
            var comparison = await _viewModel.GetProviderComparisonAsync(symbol);
            ShowProviderComparisonDialog(comparison.Symbol, comparison.Providers);
        }
    }

    private void SymbolFilter_TextChanged(object sender, TextChangedEventArgs e)
        => _viewModel.ApplySymbolFilter(SymbolFilterBox.Text?.Trim() ?? string.Empty);

    private void SymbolQuality_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SymbolQualityList.SelectedItem is SymbolQualityModel selected)
        {
            _viewModel.ShowSymbolDrilldown(selected);
        }
        else
        {
            _viewModel.HideSymbolDrilldown();
        }
    }

    private void CloseDrilldown_Click(object sender, RoutedEventArgs e)
    {
        SymbolQualityList.SelectedItem = null;
        _viewModel.HideSymbolDrilldown();
    }

    private void SeverityFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        var severity = (SeverityFilterCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "All";
        _viewModel.ApplyAlertFilter(severity);
    }

    private async void AcknowledgeAlert_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string alertId)
        {
            await _viewModel.AcknowledgeAlertAsync(alertId);
        }
    }

    private async void AcknowledgeAll_Click(object sender, RoutedEventArgs e) => await _viewModel.AcknowledgeAllAlertsAsync();

    private void AnomalyType_Changed(object sender, SelectionChangedEventArgs e)
    {
        var type = (AnomalyTypeCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "All";
        _viewModel.ApplyAnomalyFilter(type);
    }

    private void RenderTrendChart(IReadOnlyList<TrendPoint> points)
    {
        if (points.Count == 0)
        {
            TrendChartLine.Points = new PointCollection();
            TrendChartFill.Points = new PointCollection();
            XAxisLabels.Children.Clear();
            return;
        }

        var width = TrendChart.ActualWidth > 0 ? TrendChart.ActualWidth : 600;
        var height = TrendChart.ActualHeight > 0 ? TrendChart.ActualHeight : 200;
        var maxScore = Math.Max(100, points.Max(p => p.Score));
        var minScore = Math.Min(0, points.Min(p => p.Score));

        var linePoints = new PointCollection();
        var fillPoints = new PointCollection();

        for (var i = 0; i < points.Count; i++)
        {
            var x = i * (width / Math.Max(1, points.Count - 1));
            var normalized = (points[i].Score - minScore) / Math.Max(1, maxScore - minScore);
            var y = height - normalized * height;
            linePoints.Add(new Point(x, y));
            fillPoints.Add(new Point(x, y));
        }

        fillPoints.Add(new Point(width, height));
        fillPoints.Add(new Point(0, height));

        TrendChartLine.Points = linePoints;
        TrendChartFill.Points = fillPoints;

        XAxisLabels.Children.Clear();
        foreach (var label in points.Select(p => p.Label))
        {
            XAxisLabels.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = (Brush)Resources["ConsoleTextMutedBrush"],
                Margin = new Thickness(0, 0, 16, 0)
            });
        }
    }

    private void ApplyDrilldownHeatmap()
    {
        var heatmapCells = new[] { HeatmapCell0, HeatmapCell1, HeatmapCell2, HeatmapCell3, HeatmapCell4, HeatmapCell5, HeatmapCell6 };
        var dayLabels = new[] { HeatmapDay0Label, HeatmapDay1Label, HeatmapDay2Label, HeatmapDay3Label, HeatmapDay4Label, HeatmapDay5Label, HeatmapDay6Label };

        for (var i = 0; i < heatmapCells.Length; i++)
        {
            if (i >= _viewModel.DrilldownHeatmapCells.Count)
            {
                heatmapCells[i].Background = Brushes.Transparent;
                heatmapCells[i].ToolTip = null;
                dayLabels[i].Text = string.Empty;
                continue;
            }

            var cell = _viewModel.DrilldownHeatmapCells[i];
            dayLabels[i].Text = cell.Label;
            heatmapCells[i].Background = cell.Tone switch
            {
                DataQualityVisualTones.Success => new SolidColorBrush(Color.FromArgb(200, 63, 185, 80)),
                DataQualityVisualTones.Info => new SolidColorBrush(Color.FromArgb(200, 78, 201, 176)),
                DataQualityVisualTones.Warning => new SolidColorBrush(Color.FromArgb(200, 227, 179, 65)),
                _ => new SolidColorBrush(Color.FromArgb(200, 244, 67, 54))
            };
            heatmapCells[i].ToolTip = cell.Tooltip;
        }
    }

    private static string? PromptForQualityCheckPath()
        => Microsoft.VisualBasic.Interaction.InputBox("Enter a symbol or path to check", "Run Quality Check", "SPY");

    private static bool ShowConfirmation(string title, string message)
        => MessageBox.Show(message, title, MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;

    private static void ShowProviderComparisonDialog(string symbol, IReadOnlyList<DataQualityProviderComparisonItem> providers)
    {
        var lines = providers.Select(p => $"{p.Name}: {p.CompletenessText}, {p.LatencyText}, {p.FreshnessText}, {p.Status}");
        MessageBox.Show(string.Join(Environment.NewLine, lines), $"Provider Comparison - {symbol}", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
