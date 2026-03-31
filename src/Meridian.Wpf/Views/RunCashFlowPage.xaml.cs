using System.Windows.Controls;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

public partial class RunCashFlowPage : Page
{
    public RunCashFlowPage()
    {
        InitializeComponent();
        DataContext = new CashFlowViewModel();
    }
}
