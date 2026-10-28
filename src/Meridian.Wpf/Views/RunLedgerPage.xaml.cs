using System.Windows.Controls;
using Meridian.Wpf.ViewModels;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Views;

/// <summary>
/// Provides the desktop page to review run postings and execution-linked cash movement.
/// </summary>
public partial class RunLedgerPage : Page
{
    public RunLedgerPage()
    {
        InitializeComponent();
        DataContext = new StrategyRunLedgerViewModel(
            WpfServices.StrategyRunWorkspaceService.Instance,
            WpfServices.NavigationService.Instance);
    }
}
