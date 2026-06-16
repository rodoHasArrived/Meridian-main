using System;
using System.Collections.Generic;
using Meridian.Wpf.Models;
using Meridian.Wpf.Shell.Services;
using Meridian.Wpf.ViewModels;
using Meridian.Wpf.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Wpf.Features.Home;

public sealed class HomeFeatureModule : IDesktopFeatureModule
{
    private static readonly IReadOnlyList<ShellPageDescriptor> Pages =
    [
        ShellPageRegistryBuilder.Page<HomeWorkspacePage>(
            "HomeWorkspace",
            "Home",
            "Review operational readiness before opening deep workspaces.",
            "strategy",
            "Launchpad",
            "\uE80F",
            -10,
            ShellNavigationVisibilityTier.Primary,
            ["home", "launch", "readiness", "provider", "reconciliation", "approvals", "reporting"],
            ["TradingShell", "PortfolioShell", "AccountingShell", "ReportingShell", "StrategyShell", "DataShell", "SettingsShell"],
            ["Home", "WorkstationHome"],
            hideFromDefaultPalette: true)
    ];

    public void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTransient<HomeWorkspaceViewModel>();
        services.AddTransient<HomeWorkspacePage>();
    }

    public IReadOnlyList<ShellPageDescriptor> DescribePages() => Pages;
}
