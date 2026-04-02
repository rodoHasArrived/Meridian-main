using System.Windows.Controls;
using Meridian.Wpf.ViewModels;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Views;

public partial class RunDetailPage : Page
{
    public RunDetailPage()
    {
        InitializeComponent();
        DataContext = new StrategyRunDetailViewModel(
            WpfServices.StrategyRunWorkspaceService.Instance,
            WpfServices.NavigationService.Instance);
    }
}
