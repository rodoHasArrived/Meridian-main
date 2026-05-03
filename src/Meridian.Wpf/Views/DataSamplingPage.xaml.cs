using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

public partial class DataSamplingPage : Page
{
    private readonly DataSamplingViewModel _viewModel = new();

    public DataSamplingPage()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        _viewModel.Initialize();
    }
}
