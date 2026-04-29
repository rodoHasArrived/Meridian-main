using Meridian.Wpf.Models;
using Meridian.Ui.Services;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Wpf.Services;

public static class WpfShellServiceCollectionExtensions
{
    public static IServiceCollection AddMeridianWpfShell(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

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

        foreach (var pageType in ShellNavigationCatalog.GetRegisteredPageTypes())
        {
            services.AddTransient(pageType);
        }

        foreach (var shellDefinition in ShellNavigationCatalog.WorkspaceShells)
        {
            if (shellDefinition.StateProviderType is not null)
            {
                services.AddTransient(shellDefinition.StateProviderType);
            }

            if (shellDefinition.ViewModelType is not null)
            {
                services.AddTransient(shellDefinition.ViewModelType);
            }
        }

        return services;
    }
}
