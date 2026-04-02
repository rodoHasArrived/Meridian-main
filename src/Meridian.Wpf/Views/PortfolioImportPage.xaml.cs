using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

public partial class PortfolioImportPage : Page
{
    private readonly PortfolioImportViewModel _viewModel = new();

    public PortfolioImportPage()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        // DataContext already bound; nothing extra needed.
    }

    private void BrowseFile_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.BrowseFile();
    }

    private async void ImportFile_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.ImportFileAsync();
    }

    private async void ImportIndex_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string indexId) return;
        await _viewModel.ImportIndexAsync(indexId, btn.Content?.ToString() ?? indexId);
    }

    private async void AddManualSymbols_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.AddManualSymbolsAsync();
    }
}
