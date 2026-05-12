using Meridian.Wpf.Models;
using Meridian.Wpf.Features;
using Meridian.Wpf.Shell.Refresh;
using Meridian.Wpf.Shell.Root;
using Meridian.Wpf.Shell.Services;
using Meridian.Wpf.Shell.Session;
using Meridian.Wpf.Shell.ViewModels;
using Meridian.Ui.Services;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Wpf.Services;

public static class WpfShellServiceCollectionExtensions
{
    public static IServiceCollection AddMeridianWpfShell(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IShellRouteRegistry, ShellRouteRegistry>();
        services.AddSingleton<IWindowStateStore, WindowStateStore>();
        services.AddSingleton<DesktopShellSessionService>();
        services.AddSingleton<DesktopLaunchRouter>();
        services.AddSingleton<FileDropRouter>();
        services.AddSingleton<DesktopShellCoordinator>();
        services.AddTransient<ShellRefreshCoordinator>();
        services.AddTransient<CommandPaletteViewModel>();
        services.AddTransient<OperatorInboxViewModel>();
        services.AddTransient<WorkflowSummaryStripViewModel>();
        services.AddTransient<IPageContentFactory, PageContentFactory>();
        services.AddTransient<IShellNavigationCoordinator, ShellNavigationCoordinator>();
        services.AddTransient<PaneHostViewModel>();
        services.AddTransient<Meridian.Wpf.ViewModels.MainPageViewModel>();
        services.AddTransient<Meridian.Wpf.Views.MainPage>();
        services.AddTransient(sp => new ResearchWorkspaceShellPresentationService(
            sp.GetRequiredService<StrategyRunWorkspaceService>(),
            sp.GetRequiredService<IResearchBriefingWorkspaceService>(),
            sp.GetRequiredService<WatchlistService>(),
            sp.GetRequiredService<FundContextService>(),
            sp.GetService<WorkstationOperatingContextService>(),
            sp.GetRequiredService<WorkspaceShellContextService>(),
            sp.GetService<WorkstationWorkflowSummaryService>(),
            sp.GetService<Meridian.Strategies.Services.PromotionService>()));
        services.AddTransient<TradingWorkspaceShellPresentationService>();
        services.AddMeridianWpfFeatureModules();

        foreach (var pageType in ShellNavigationCatalog.GetRegisteredPageTypes())
        {
            AddTransientIfMissing(services, pageType);
        }

        foreach (var shellDefinition in ShellNavigationCatalog.WorkspaceShells)
        {
            if (shellDefinition.StateProviderType is not null)
            {
                AddTransientIfMissing(services, shellDefinition.StateProviderType);
            }

            if (shellDefinition.ViewModelType is not null)
            {
                AddTransientIfMissing(services, shellDefinition.ViewModelType);
            }
        }

        return services;
    }

    private static void AddTransientIfMissing(IServiceCollection services, Type serviceType)
    {
        if (services.Any(descriptor => descriptor.ServiceType == serviceType))
        {
            return;
        }

        services.AddTransient(serviceType);
    }
}
