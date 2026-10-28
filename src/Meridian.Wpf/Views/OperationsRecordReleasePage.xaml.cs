using System;
using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

/// <summary>
/// Provides the desktop page to follow the release path from source data through accounting record to report pack.
/// </summary>
public partial class OperationsRecordReleasePage : Page
{
    private readonly OperationsRecordReleaseViewModel _viewModel;

    public OperationsRecordReleasePage(OperationsRecordReleaseViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = _viewModel;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
        => _viewModel.Activate();

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
        => _viewModel.Dispose();
}
