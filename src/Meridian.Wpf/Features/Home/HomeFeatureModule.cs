using System;
using System.Collections.Generic;
using Meridian.Ui.Shared.Services;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
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
            hideFromDefaultPalette: true),
        ShellPageRegistryBuilder.Page<OperatorReadinessConsolePage>(
            "OperatorReadinessConsole",
            "Operator readiness",
            "Review cross-workspace readiness gates and operator work items.",
            "strategy",
            "Launchpad",
            "\uE9D9",
            -5,
            ShellNavigationVisibilityTier.Primary,
            ["readiness", "operator", "console", "gates", "work items", "inbox", "cross-workspace"],
            ["HomeWorkspace", "TradingShell", "AccountingShell", "ReportingShell", "DataShell"])
    ];

    public void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTransient<HomeWorkspaceViewModel>();
        services.AddTransient<HomeWorkspacePage>();
        // Null-tolerant factory: each readiness source is owned by another feature module, and the
        // console degrades per-panel when a source is absent from the composition.
        services.AddTransient(static sp => new OperatorReadinessConsoleViewModel(
            sp.GetService<TradingOperatorReadinessService>(),
            sp.GetService<IWorkstationOperatorInboxApiClient>(),
            sp.GetService<IWorkstationReconciliationApiClient>(),
            sp.GetService<StrategyRunWorkspaceService>(),
            workflowActionCatalog: sp.GetService<Meridian.Ui.Shared.Workflows.IWorkflowActionCatalog>()));
        services.AddTransient<OperatorReadinessConsolePage>();
    }

    public IReadOnlyList<ShellPageDescriptor> DescribePages() => Pages;
}
