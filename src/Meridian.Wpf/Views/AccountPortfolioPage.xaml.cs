using System.Windows;
using System.Windows.Controls;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

/// <summary>
/// Code-behind for <see cref="AccountPortfolioPage"/>.
/// Wires the ViewModel and delegates lifecycle to it; no business logic here.
/// </summary>
public partial class AccountPortfolioPage : Page
{
    private readonly AccountPortfolioViewModel _viewModel;
    private readonly string _accountId;

    public AccountPortfolioPage(string accountId)
    {
        InitializeComponent();
        _accountId = accountId;
        _viewModel = new AccountPortfolioViewModel(ApiClientService.Instance);
        DataContext = _viewModel;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        _ = _viewModel.InitializeAsync(_accountId);
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        _viewModel.Dispose();
    }
}
