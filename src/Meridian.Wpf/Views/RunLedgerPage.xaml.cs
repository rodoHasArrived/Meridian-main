using System.Windows.Controls;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

public partial class RunLedgerPage : Page
{
    public RunLedgerPage()
    {
        InitializeComponent();
        DataContext = new StrategyRunLedgerViewModel();
    }
}
