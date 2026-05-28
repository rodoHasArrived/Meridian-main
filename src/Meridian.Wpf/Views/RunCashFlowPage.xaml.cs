using System.Windows.Controls;
using Meridian.Strategies.Services;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;
using WpfNavigationService = Meridian.Wpf.Services.NavigationService;

namespace Meridian.Wpf.Views;

public partial class RunCashFlowPage : Page
{
    public RunCashFlowPage()
        : this(
            ResolveRequired<StrategyRunWorkspaceService>(),
            ResolveRequired<WpfNavigationService>(),
            App.Services?.GetService(typeof(StrategyRunContinuityService)) as StrategyRunContinuityService)
    {
    }

    public RunCashFlowPage(
        StrategyRunWorkspaceService workspaceService,
        WpfNavigationService navigationService,
        StrategyRunContinuityService? continuityService = null)
    {
        InitializeComponent();
        DataContext = new CashFlowViewModel(workspaceService, navigationService, continuityService);
    }

    private static T ResolveRequired<T>() where T : notnull
    {
        if (App.Services is null)
        {
            throw new InvalidOperationException(
                $"{nameof(RunCashFlowPage)} requires the application service provider for parameterless construction.");
        }

        return App.Services.GetService(typeof(T)) is T service
            ? service
            : throw new InvalidOperationException(
                $"{nameof(RunCashFlowPage)} could not resolve required service '{typeof(T).Name}'.");
    }
}
