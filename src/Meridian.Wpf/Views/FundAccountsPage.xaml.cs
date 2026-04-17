using System.Windows;
using System.Windows.Controls;
using Meridian.Contracts.FundStructure;
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

    private async void AccountSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox { SelectedItem: AccountSummaryDto selectedAccount })
            return;

        _viewModel.SelectedAccount = selectedAccount;
        await _viewModel.InspectSelectedAccountAsync();
    }
}
