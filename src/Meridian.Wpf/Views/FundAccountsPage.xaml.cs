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
    {
        try
        {
            await _viewModel.LoadFundAccountsAsync();
        }
        catch (System.OperationCanceledException)
        {
            // Navigation cancelled the in-flight load before it completed; benign during teardown.
            global::Meridian.Wpf.Services.LoggingService.Instance.LogDebug(
                "Page load cancelled during navigation.",
                ("page", GetType().Name));
        }
        catch (System.Exception ex)
        {
            global::Meridian.Wpf.Services.LoggingService.Instance.LogError("Fund Accounts page failed to load.", ex);
        }
    }
}
