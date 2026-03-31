using System.Windows.Controls;
using Meridian.Wpf.ViewModels;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Views;

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
