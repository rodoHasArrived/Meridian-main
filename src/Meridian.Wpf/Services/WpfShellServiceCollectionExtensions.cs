using Meridian.Wpf.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Wpf.Services;

public static class WpfShellServiceCollectionExtensions
{
    public static IServiceCollection AddMeridianWpfShell(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTransient<Meridian.Wpf.ViewModels.MainPageViewModel>();
        services.AddTransient<Meridian.Wpf.Views.MainPage>();
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
