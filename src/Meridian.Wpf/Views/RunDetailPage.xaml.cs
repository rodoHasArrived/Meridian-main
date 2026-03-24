using System.Windows.Controls;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

public partial class RunDetailPage : Page
{
    public RunDetailPage()
    {
        InitializeComponent();
        DataContext = new StrategyRunDetailViewModel();
    }
}
