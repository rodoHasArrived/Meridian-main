using System;
using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

public partial class OperatorReadinessConsolePage : Page
{
    private readonly OperatorReadinessConsoleViewModel _viewModel;

    public OperatorReadinessConsolePage(OperatorReadinessConsoleViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = _viewModel;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
        => _viewModel.Activate();

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
        => _viewModel.Deactivate();
}
