using System.Windows.Controls;
using Meridian.Wpf.ViewModels;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Views;

/// <summary>
/// Provides the desktop page to review diagnostics and execution details for a selected run.
/// </summary>
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
