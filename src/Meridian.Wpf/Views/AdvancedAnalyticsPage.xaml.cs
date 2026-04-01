using System.Windows;
using System.Windows.Controls;
using Meridian.Ui.Services;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

/// <summary>
/// Page for advanced analytics including gap analysis, cross-provider comparison,
/// latency histograms, anomaly detection, and detailed quality reports.
/// All state is bound directly to <see cref="AdvancedAnalyticsViewModel"/> via XAML.
/// MVVM compliant: code-behind contains only constructor DI wiring and minimal event-handler
/// delegation to the ViewModel — no direct UI element mutation.
/// </summary>
public partial class AdvancedAnalyticsPage : Page
{
    private readonly AdvancedAnalyticsViewModel _viewModel;

    public AdvancedAnalyticsPage(AdvancedAnalyticsServiceBase analyticsService)
    {
        InitializeComponent();
        _viewModel = new AdvancedAnalyticsViewModel(analyticsService);
        DataContext = _viewModel;
        Loaded += AdvancedAnalyticsPage_Loaded;
    }

    private async void AdvancedAnalyticsPage_Loaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
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
        await _viewModel.CompareProvidersAsync();
    }

    private void CloseInfoBar_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.DismissStatus();
    }
}
