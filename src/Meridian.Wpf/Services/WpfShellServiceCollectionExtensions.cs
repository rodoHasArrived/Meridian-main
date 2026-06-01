using Meridian.Wpf.Models;
using Meridian.Wpf.Features;
using Meridian.Wpf.Shell.Refresh;
using Meridian.Wpf.Shell.Root;
using Meridian.Wpf.Shell.Services;
using Meridian.Wpf.Shell.Session;
using Meridian.Wpf.Shell.ViewModels;
using Meridian.Ui.Services;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Wpf.Services;

public static class WpfShellServiceCollectionExtensions
{
    public static IServiceCollection AddWorkspaceScoped<TService>(this IServiceCollection services)
        where TService : class, IWorkspaceScopedService
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<TService>();
        return services;
    }

    public static IServiceCollection AddWorkspaceScoped<TService, TImplementation>(this IServiceCollection services)
        where TService : class
        where TImplementation : class, TService, IWorkspaceScopedService
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<TService, TImplementation>();
        return services;
    }

    public static IServiceCollection AddWorkspaceShellSlotContributor<TContributor>(this IServiceCollection services)
        where TContributor : class, IWorkspaceShellSlotContributor
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IWorkspaceShellSlotContributor, TContributor>();
        return services;
    }

    public static IServiceCollection AddMeridianWpfShell(this IServiceCollection services)
        => AddMeridianWpfShell(services, configuration: null);

    public static IServiceCollection AddMeridianWpfShell(this IServiceCollection services, IConfiguration? configuration)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IShellRouteRegistry, ShellRouteRegistry>();
        services.AddSingleton<ViewModelViewResolver>();
        services.AddSingleton<IViewModelViewResolver>(sp => sp.GetRequiredService<ViewModelViewResolver>());
        services.AddSingleton<IWindowStateStore, WindowStateStore>();
        AddSingletonIfMissing<IWorkspaceShellSlotContributionService, WorkspaceShellSlotContributionService>(services);
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
        services.AddWorkspaceScoped<StrategyWorkspaceShellPresentationService>();
        if (configuration is null)
        {
            services.AddMeridianWpfFeatureModules();
        }
        else
        {
            services.AddMeridianWpfFeatureModules(configuration);
        }
        AddTransientIfMissing(services, typeof(Meridian.Wpf.Services.AccountingWorkspaceShellStateProvider));
        AddTransientIfMissing(services, typeof(Meridian.Wpf.ViewModels.AccountingWorkspaceShellViewModel));
        AddTransientIfMissing(services, typeof(Meridian.Wpf.Views.AccountingWorkspaceShellPage));

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

    private static void AddSingletonIfMissing<TService, TImplementation>(IServiceCollection services)
        where TService : class
        where TImplementation : class, TService
    {
        if (services.Any(static descriptor => descriptor.ServiceType == typeof(TService)))
        {
            return;
        }

        services.AddSingleton<TService, TImplementation>();
    }
}
