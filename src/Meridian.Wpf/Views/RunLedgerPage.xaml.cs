using System.Windows.Controls;
using Meridian.Wpf.ViewModels;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Views;

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
