using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Meridian.Contracts.Workstation;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

public partial class SecurityMasterPage : Page
{
    private readonly SecurityMasterViewModel _viewModel;

    public SecurityMasterPage(SecurityMasterViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;

        Unloaded += (_, _) => _viewModel.Stop();
    }

    private async void Search_Click(object sender, RoutedEventArgs e)
        => await _viewModel.SearchAsync();

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            _ = _viewModel.SearchAsync();
    }

    private async void ResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel.SelectedSecurity is SecurityMasterWorkstationDto selected)
            await _viewModel.LoadSelectedTrustSnapshotAsync(selected.SecurityId);
    }
}
