using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

public partial class AnalysisExportPage : Page
{
    private readonly AnalysisExportViewModel _viewModel = new();

    public AnalysisExportPage()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        _viewModel.Initialize();
    }

    private void RunExport_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.RunExport();
    }

    private void SavePreset_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SavePreset();
    }
}
