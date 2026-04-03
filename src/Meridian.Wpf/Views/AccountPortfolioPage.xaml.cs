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
        // InitializeAsync is triggered via the Parameter property set by NavigationService.
        // If no parameter was passed (e.g. direct instantiation), initialize with empty id.
        if (string.IsNullOrEmpty(_viewModel.AccountId))
            _ = _viewModel.InitializeAsync(_viewModel.AccountId);
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        _viewModel.Dispose();
    }
}
