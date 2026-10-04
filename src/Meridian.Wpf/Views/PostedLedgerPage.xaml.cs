using System;
using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

public partial class PostedLedgerPage : Page
{
    private readonly PostedLedgerViewModel _viewModel;

    public PostedLedgerPage(PostedLedgerViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = _viewModel;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
        => _viewModel.Activate();

    // Deactivate, not Dispose: the shell's Frame can restore this page instance from navigation
    // history, and a disposed view model returns permanently inert. The container owns the
    // view model's lifetime and disposes it when the scope ends.
    private void OnPageUnloaded(object sender, RoutedEventArgs e)
        => _viewModel.Deactivate();
}
