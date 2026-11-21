using Meridian.Contracts.Workstation;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using Meridian.Identity.Auth;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Cross-screen counts (A3): open orders, provider health and open breaks, counted once with
/// <see cref="CrossScreenCountDefinitions"/> over the same sources the Trading, Data and Accounting
/// workspaces read, so Settings, the shell, every workspace and the WPF desktop show one number.
/// </summary>
public static partial class WorkstationEndpoints
{
    private static readonly UserPermission[] CrossScreenProviderPermissions =
        [UserPermission.ViewHistoricalData, UserPermission.ViewDiagnostics, UserPermission.ManageStorage];

    private static readonly UserPermission[] CrossScreenBreakPermissions =
        [UserPermission.ViewTrades, UserPermission.ViewDirectLending, UserPermission.ManageDirectLending,
         UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster, UserPermission.AdminMaintenance];

    /// <summary>
    /// Each count is gated by the permission of the workspace route that owns its source, so the
    /// route can admit the union without handing a Data-only caller the trading book's order count.
    /// A count the caller may not read, or whose source is not registered, is null.
    /// </summary>
    private static async Task<WorkstationCrossScreenCountsDto> BuildCrossScreenCountsAsync(
        Guid? fundAccountId,
        HttpContext context)
    {
        var openOrders = await CountCrossScreenOpenOrdersAsync(fundAccountId, context).ConfigureAwait(false);
        var providers = await CountCrossScreenProvidersAsync(context).ConfigureAwait(false);
        var openBreaks = await CountCrossScreenOpenBreaksAsync(context).ConfigureAwait(false);
        return new WorkstationCrossScreenCountsDto(DateTimeOffset.UtcNow, openOrders, providers, openBreaks);
    }

    private static async Task<WorkstationCrossScreenCount?> CountCrossScreenOpenOrdersAsync(Guid? fundAccountId, HttpContext context)
    {
        if (!HasPermission(context, UserPermission.ViewTrades)
            || !await CanReadTradingAccountAsync(fundAccountId, context).ConfigureAwait(false))
        {
            return null;
        }

        var oms = context.RequestServices.GetService<IOrderManager>();
        if (oms is null)
        {
            return null;
        }

        var brokerPortfolio = context.RequestServices.GetService<IPortfolioState>() as BrokeragePortfolioState;
        if (!await CanReadBrokerPortfolioAsync(brokerPortfolio, fundAccountId, context).ConfigureAwait(false))
        {
            // The trading blotter hides this book too, and shows no orders, until the owning
            // account is selected and authorized.
            oms = null;
        }

        var visible = SelectVisibleOpenOrders(oms, brokerPortfolio, fundAccountId);
        return new WorkstationCrossScreenCount(visible.Count, CrossScreenCountDefinitions.OpenOrdersDefinition);
    }

    private static async Task<WorkstationProviderHealthCount?> CountCrossScreenProvidersAsync(HttpContext context)
    {
        if (!CrossScreenProviderPermissions.Any(permission => HasPermission(context, permission)))
        {
            return null;
        }

        var configStore = context.RequestServices.GetService<Meridian.Application.UI.ConfigStore>();
        if (configStore is null && context.RequestServices.GetService<ProviderConnectionLifecycleService>() is null)
        {
            return null;
        }

        var providers = await BuildDataProviderRecordsAsync(context, configStore).ConfigureAwait(false);
        var (healthy, total) = CrossScreenCountDefinitions.CountProviderHealth(providers.Select(static provider => provider.Status));
        return new WorkstationProviderHealthCount(healthy, total, CrossScreenCountDefinitions.ProvidersDefinition);
    }

    private static async Task<WorkstationCrossScreenCount?> CountCrossScreenOpenBreaksAsync(HttpContext context)
    {
        if (!CrossScreenBreakPermissions.Any(permission => HasPermission(context, permission)))
        {
            return null;
        }

        var breakQueueRepository = context.RequestServices.GetService<IReconciliationBreakQueueRepository>();
        if (breakQueueRepository is null || !TryResolveReconciliationBreakQueueScope(context, out var queueScope))
        {
            return null;
        }

        // The accounting workspace's headline reads every account in the queue scope; so does this.
        var items = await GetBreakQueueItemsAsync(
                breakQueueRepository,
                queueScope,
                status: null,
                fundAccountId: null,
                ledgerBookId: null,
                ct: context.RequestAborted)
            .ConfigureAwait(false);
        return new WorkstationCrossScreenCount(
            CrossScreenCountDefinitions.CountOpenBreaks(items),
            CrossScreenCountDefinitions.OpenBreaksDefinition);
    }
}
