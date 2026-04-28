using System.Windows;
using System.Windows.Controls;
using Meridian.Ui.Services;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

/// <summary>
/// Page for advanced analytics including gap analysis, cross-provider comparison,
/// latency histograms, anomaly detection, and detailed quality reports.
/// All state is bound directly to <see cref="AdvancedAnalyticsViewModel"/> via XAML.
/// MVVM compliant: code-behind contains only constructor DI wiring and page-load initialization.
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
}
