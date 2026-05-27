using System.Windows.Controls;
using Meridian.Strategies.Services;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Wpf.Views;

public partial class RunCashFlowPage : Page
{
    public RunCashFlowPage()
    {
        InitializeComponent();
        DataContext = new CashFlowViewModel(
            App.Services.GetRequiredService<StrategyRunWorkspaceService>(),
            App.Services.GetRequiredService<NavigationService>(),
            App.Services.GetService<StrategyRunContinuityService>());
    }
}
