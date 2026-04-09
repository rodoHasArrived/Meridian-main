using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

/// <summary>
/// Code-behind for <see cref="AccountPortfolioPage"/>.
/// Wires the ViewModel and delegates lifecycle to it; no business logic here.
/// </summary>
public partial class AccountPortfolioPage : Page
{
    private readonly AccountPortfolioViewModel _viewModel;

    public AccountPortfolioPage(AccountPortfolioViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = _viewModel;
        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        // Initialization is triggered by the Parameter property setter when NavigationService
        // passes the accountId string after page creation. Nothing to do here.
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        _viewModel.Dispose();
    }
}
