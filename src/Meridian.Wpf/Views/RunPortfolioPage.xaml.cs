using System.Windows.Controls;
using Meridian.Wpf.ViewModels;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Views;

/// <summary>
/// Provides the desktop page to review holdings, exposures, and weights for the selected run.
/// </summary>
public partial class RunPortfolioPage : Page
{
    public RunPortfolioPage()
    {
        InitializeComponent();
        DataContext = new StrategyRunPortfolioViewModel(
            WpfServices.StrategyRunWorkspaceService.Instance,
            WpfServices.NavigationService.Instance);
    }
}
