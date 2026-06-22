using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

public partial class FundAccountsPage : Page
{
    private readonly FundAccountsViewModel _viewModel;

    public FundAccountsPage(FundAccountsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
        => await _viewModel.LoadFundAccountsAsync();
}
