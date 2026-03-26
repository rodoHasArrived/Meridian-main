using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

public partial class ExportPresetsPage : Page
{
    private readonly ExportPresetsViewModel _viewModel = new();

    public ExportPresetsPage()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
    }

    private void SavePreset_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SavePreset();
    }

    private void DeletePreset_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.DeletePreset();
    }
}
