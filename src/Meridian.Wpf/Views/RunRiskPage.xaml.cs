using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

public partial class RunRiskPage
{
    private readonly RunRiskViewModel _viewModel;

    internal RunRiskPage(RunRiskViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = _viewModel;
    }
}
