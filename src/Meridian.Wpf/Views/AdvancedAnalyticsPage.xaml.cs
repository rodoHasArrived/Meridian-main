using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Meridian.Ui.Services;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

/// <summary>
/// Page for advanced analytics including gap analysis, cross-provider comparison,
/// latency histograms, anomaly detection, and detailed quality reports.
/// </summary>
public partial class AdvancedAnalyticsPage : Page
{
    private readonly AdvancedAnalyticsViewModel _viewModel;

    public AdvancedAnalyticsPage(AdvancedAnalyticsServiceBase analyticsService)
    {
        InitializeComponent();
        _viewModel = new AdvancedAnalyticsViewModel(analyticsService);
        DataContext = _viewModel;

        // Wire ObservableCollections to named controls (transitional until XAML uses {Binding})
        SymbolQualityList.ItemsSource = _viewModel.SymbolQualityItems;
        RecommendationsList.ItemsSource = _viewModel.Recommendations;
        GapsList.ItemsSource = _viewModel.GapItems;
        DiscrepanciesList.ItemsSource = _viewModel.DiscrepancyItems;
        LatencyList.ItemsSource = _viewModel.LatencyItems;
        RateLimitsList.ItemsSource = _viewModel.RateLimitItems;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        CompareDatePicker.SelectedDate = _viewModel.CompareDate;
        Loaded += AdvancedAnalyticsPage_Loaded;
    }

    private async void AdvancedAnalyticsPage_Loaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
        SyncGapSymbolCombo();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.RefreshAllAsync();
    }

    private async void GenerateReport_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.LoadQualityReportAsync();
        _viewModel.ShowSuccess("Quality report generated.");
    }

    private async void AnalyzeGaps_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.GapSymbol = (GapSymbolCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty;
        await _viewModel.AnalyzeGapsAsync();
    }

    private async void RepairGaps_Click(object sender, RoutedEventArgs e)
    {
        var dialogResult = MessageBox.Show(
            "This will attempt to fetch missing data from alternative providers. Continue?",
            "Repair Data Gaps",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (dialogResult == MessageBoxResult.Yes)
        {
            await _viewModel.RepairGapsAsync();
        }
    }

    private async void CompareProviders_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.CompareSymbol = CompareSymbolBox.Text ?? string.Empty;
        _viewModel.CompareDate = CompareDatePicker.SelectedDate ?? DateTime.Today;
        await _viewModel.CompareProvidersAsync();
    }

    private void CloseInfoBar_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.DismissStatus();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AdvancedAnalyticsViewModel.IsRefreshBusy):
                RefreshButton.IsEnabled = !_viewModel.IsRefreshBusy;
                break;

            case nameof(AdvancedAnalyticsViewModel.OverallScoreText):
                OverallScoreText.Text = _viewModel.OverallScoreText;
                break;

            case nameof(AdvancedAnalyticsViewModel.GradeText):
                GradeText.Text = _viewModel.GradeText;
                UpdateGradeBadge(_viewModel.GradeText);
                break;

            case nameof(AdvancedAnalyticsViewModel.CompletenessText):
                CompletenessText.Text = _viewModel.CompletenessText;
                break;

            case nameof(AdvancedAnalyticsViewModel.IsRecommendationsVisible):
                RecommendationsCard.Visibility = _viewModel.IsRecommendationsVisible
                    ? Visibility.Visible : Visibility.Collapsed;
                break;

            case nameof(AdvancedAnalyticsViewModel.IsGapAnalysisBusy):
                GapAnalysisProgress.Visibility = _viewModel.IsGapAnalysisBusy
                    ? Visibility.Visible : Visibility.Collapsed;
                break;

            case nameof(AdvancedAnalyticsViewModel.IsGapSummaryVisible):
                GapSummaryCard.Visibility = _viewModel.IsGapSummaryVisible
                    ? Visibility.Visible : Visibility.Collapsed;
                break;

            case nameof(AdvancedAnalyticsViewModel.TotalGapsText):
                TotalGapsText.Text = _viewModel.TotalGapsText;
                TotalGapDurationText.Text = _viewModel.TotalGapDurationText;
                GapCountText.Text = _viewModel.TotalGapsText;
                break;

            case nameof(AdvancedAnalyticsViewModel.RepairableGapsText):
                RepairableGapsText.Text = _viewModel.RepairableGapsText;
                RepairGapsButton.IsEnabled = _viewModel.CanRepairGaps;
                break;

            case nameof(AdvancedAnalyticsViewModel.IsComparisonBusy):
                ComparisonProgress.Visibility = _viewModel.IsComparisonBusy
                    ? Visibility.Visible : Visibility.Collapsed;
                break;

            case nameof(AdvancedAnalyticsViewModel.IsComparisonResultsVisible):
                ComparisonResultsCard.Visibility = _viewModel.IsComparisonResultsVisible
                    ? Visibility.Visible : Visibility.Collapsed;
                break;

            case nameof(AdvancedAnalyticsViewModel.ConsistencyScoreText):
                ConsistencyScoreText.Text = _viewModel.ConsistencyScoreText;
                DiscrepancyCountText.Text = _viewModel.DiscrepancyCountText;
                break;

            case nameof(AdvancedAnalyticsViewModel.AvailableSymbols):
                SyncGapSymbolCombo();
                break;

            case nameof(AdvancedAnalyticsViewModel.IsStatusVisible):
                StatusInfoBar.Visibility = _viewModel.IsStatusVisible
                    ? Visibility.Visible : Visibility.Collapsed;
                break;

            case nameof(AdvancedAnalyticsViewModel.StatusMessage):
                StatusInfoIcon.Text = _viewModel.StatusIcon;
                StatusInfoIcon.Foreground = new SolidColorBrush(_viewModel.StatusColor);
                StatusInfoTitle.Text = _viewModel.StatusTitle;
                StatusInfoMessage.Text = _viewModel.StatusMessage;
                break;
        }
    }

    // ---- Helpers ----

    private void SyncGapSymbolCombo()
    {
        GapSymbolCombo.Items.Clear();
        GapSymbolCombo.Items.Add(new ComboBoxItem { Content = "All Symbols", Tag = string.Empty });
        foreach (var symbol in _viewModel.AvailableSymbols.Skip(1)) // skip the empty sentinel
        {
            GapSymbolCombo.Items.Add(new ComboBoxItem { Content = symbol, Tag = symbol });
        }

        GapSymbolCombo.SelectedIndex = 0;
    }

    private void UpdateGradeBadge(string grade)
    {
        GradeBadge.Background = grade switch
        {
            "A" or "A+" => new SolidColorBrush(Color.FromArgb(40, 72, 187, 120)),
            "B" or "B+" => new SolidColorBrush(Color.FromArgb(40, 88, 166, 255)),
            "C" or "C+" => new SolidColorBrush(Color.FromArgb(40, 237, 137, 54)),
            _ => new SolidColorBrush(Color.FromArgb(40, 245, 101, 101))
        };
    }
}
