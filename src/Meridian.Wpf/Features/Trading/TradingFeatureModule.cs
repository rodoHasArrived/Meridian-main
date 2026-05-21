using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;
using Meridian.Wpf.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Wpf.Features.Trading;

public sealed class TradingFeatureModule : IDesktopFeatureModule
{
    public void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTransient<TradingWorkspaceShellPresentationService>();
        services.AddTransient<TradingWorkspaceShellStateProvider>();
        services.AddTransient<TradingWorkspaceShellViewModel>();
        services.AddTransient<TradingWorkspaceShellPage>();
    }
}
