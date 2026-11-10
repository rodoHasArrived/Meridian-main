using System.Windows.Controls;
using Meridian.Contracts.Workstation;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

/// <summary>
/// Hosts security-master discovery, trust review, and conflict workflows.
/// </summary>
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

    private async void ResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => await global::Meridian.Wpf.Services.AsyncHandlerGuard.RunAsync(() => ResultsList_SelectionChangedCoreAsync(sender, e), nameof(ResultsList_SelectionChanged)).ConfigureAwait(true);

    private async System.Threading.Tasks.Task ResultsList_SelectionChangedCoreAsync(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel.SelectedSecurity is SecurityMasterWorkstationDto selected)
            await _viewModel.LoadSelectedTrustSnapshotAsync(selected.SecurityId);
    }
}
