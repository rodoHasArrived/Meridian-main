using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.ViewModels.Accounting;

namespace Meridian.Wpf.Views;

public partial class AccountingConfigurePage : Page
{
    private readonly AccountingConfigureViewModel _viewModel;
    private CancellationTokenSource? _loadCts;

    public AccountingConfigurePage(AccountingConfigureViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = _viewModel;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        await _viewModel.LoadAsync(_loadCts.Token);
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;
    }
}
