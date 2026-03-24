using System.Windows.Controls;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

public partial class RunPortfolioPage : Page
{
    public RunPortfolioPage()
    {
        InitializeComponent();
        DataContext = new StrategyRunPortfolioViewModel();
    }
}
