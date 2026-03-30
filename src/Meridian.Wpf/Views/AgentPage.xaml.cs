using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

/// <summary>
/// Code-behind for AgentPage.
/// Minimal implementation — only constructor and InitializeComponent.
/// All state and logic is in AgentViewModel.
/// </summary>
public partial class AgentPage : Page
{
    public AgentPage(AgentViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
