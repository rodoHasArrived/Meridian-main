using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

/// <summary>
/// Hosts risk details for the selected strategy run.
/// </summary>
public partial class RunRiskPage
{
    private readonly RunRiskViewModel _viewModel;

    public RunRiskPage(RunRiskViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = _viewModel;
    }
}
