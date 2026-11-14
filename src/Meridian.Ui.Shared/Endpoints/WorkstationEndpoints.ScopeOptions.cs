using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Services;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Operating-scope options (P4): the entities, funds and fund accounts the calling operator may
/// scope the workstation to, for the scope picker and the scope bar's names.
/// <para>
/// The route is open to any authenticated workstation operator, but the payload is not: it carries
/// only nodes the caller could already read through an existing route, decided by the same scoped
/// grants those routes use (<see cref="EndpointAuthorization.AuthorizeScopedManyAsync"/>, which
/// resolves account → fund → business → organization ancestry). A node is visible when the caller
/// holds one of these permissions globally and a scoped assignment grants it at that node or an
/// ancestor:
/// <list type="bullet">
/// <item><see cref="UserPermission.ManageFundStructure"/> for legal entities, funds and accounts
/// (the <c>/api/fund-structure/graph</c> response guard);</item>
/// <item><see cref="UserPermission.ManageDirectLending"/> for accounts (the fund-account reads in
/// <c>FundAccountEndpoints</c>, which return a fund's accounts but not the fund record);</item>
/// <item><see cref="UserPermission.ViewTrades"/> for accounts (the brokerage household and
/// portfolio-aggregation reads);</item>
/// <item><see cref="UserPermission.ViewLedgerReports"/> or
/// <see cref="UserPermission.ManageLedgerReports"/> for legal entities (the entity timeline).</item>
/// </list>
/// <see cref="UserPermission.AdminMaintenance"/> sees every node, as on the graph. Tenant
/// isolation is the fund-structure service's own: it serves only the caller's tenant snapshot and
/// refuses an unresolvable tenant, which the API exception handler reports as 403. A relationship
/// that would name a fund or entity outside the visible set is dropped rather than disclosed.
/// </para>
/// </summary>
public static partial class WorkstationEndpoints
{
    private static readonly UserPermission[] ScopeOptionReadPermissions =
    [
        UserPermission.ManageFundStructure,
        UserPermission.ManageDirectLending,
        UserPermission.ViewTrades,
        UserPermission.ViewLedgerReports,
        UserPermission.ManageLedgerReports
    ];

    private static readonly WorkstationScopeOptionsDto EmptyScopeOptions = new([], [], []);

    private static void MapScopeOptionEndpoints(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationScopeOptions), async (HttpContext context) =>
        {
            var service = context.RequestServices.GetService<IFundStructureService>();
            if (service is null)
            {
                return WorkstationServiceUnavailable("Fund structure service is not registered; scope options are unavailable.");
            }

            var options = await BuildScopeOptionsAsync(service, context, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(options, jsonOptions);
        })
        .WithName("GetWorkstationScopeOptions").RequireAuthenticatedSessionOrScopedLocalOperatorRead()
        .Produces<WorkstationScopeOptionsDto>(200)
        .Produces(401)
        .Produces(403)
        .Produces(503)
        .RequireWorkstationTenantCompanyScope();
    }

    /// <summary>Builds the caller's scope options; see the class remarks for the visibility rule.</summary>
    internal static async Task<WorkstationScopeOptionsDto> BuildScopeOptionsAsync(
        IFundStructureService service,
        HttpContext context,
        CancellationToken ct)
    {
        var isAdmin = EndpointAuthorization.HasPermission(context, UserPermission.AdminMaintenance);
        var heldPermissions = ScopeOptionReadPermissions
            .Where(permission => EndpointAuthorization.HasPermission(context, permission))
            .ToArray();
        if (!isAdmin && heldPermissions.Length == 0)
        {
            // Nothing the caller holds can grant a node, so the structure is not read at all.
            return EmptyScopeOptions;
        }

        var graph = await service
            .GetOrganizationStructureAsync(new OrganizationStructureQuery(ActiveOnly: true), ct)
            .ConfigureAwait(false);

        var entityIds = graph.Entities.Select(static entity => entity.EntityId);
        var fundIds = graph.Funds.Select(static fund => fund.FundId);
        var accountIds = graph.Accounts.Select(static account => account.AccountId);

        var visibleEntities = isAdmin
            ? entityIds.ToHashSet()
            : await AuthorizeScopeOptionIdsAsync(context, heldPermissions, AccessScopeKindDto.LegalEntity, entityIds, ct).ConfigureAwait(false);
        var visibleFunds = isAdmin
            ? fundIds.ToHashSet()
            : await AuthorizeScopeOptionIdsAsync(context, heldPermissions, AccessScopeKindDto.Fund, fundIds, ct).ConfigureAwait(false);
        var visibleAccounts = isAdmin
            ? accountIds.ToHashSet()
            : await AuthorizeScopeOptionIdsAsync(context, heldPermissions, AccessScopeKindDto.Account, accountIds, ct).ConfigureAwait(false);

        var entities = graph.Entities
            .Where(entity => visibleEntities.Contains(entity.EntityId))
            .GroupBy(static entity => entity.EntityId)
            .Select(static group => group.First())
            .Select(static entity => new WorkstationScopeEntityOptionDto(
                entity.EntityId,
                ScopeOptionLabel(entity.Name, entity.EntityId)))
            .OrderBy(static entity => entity.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static entity => entity.EntityId)
            .ToArray();

        var funds = graph.Funds
            .Where(fund => visibleFunds.Contains(fund.FundId))
            .GroupBy(static fund => fund.FundId)
            .Select(static group => group.First())
            .Select(fund => new WorkstationScopeFundOptionDto(
                fund.FundId,
                ScopeOptionLabel(fund.Name, fund.FundId),
                fund.EntityIds
                    .Where(visibleEntities.Contains)
                    .Distinct()
                    .OrderBy(static id => id)
                    .ToArray()))
            .OrderBy(static fund => fund.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static fund => fund.FundId)
            .ToArray();

        var accounts = graph.Accounts
            .Where(account => visibleAccounts.Contains(account.AccountId))
            .GroupBy(static account => account.AccountId)
            .Select(static group => group.First())
            .Select(account => new WorkstationScopeAccountOptionDto(
                account.AccountId,
                ScopeOptionLabel(account.DisplayName, account.AccountId),
                account.FundId is { } fundId && visibleFunds.Contains(fundId) ? fundId : null,
                account.EntityId is { } entityId && visibleEntities.Contains(entityId) ? entityId : null))
            .OrderBy(static account => account.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static account => account.AccountId)
            .ToArray();

        return new WorkstationScopeOptionsDto(entities, funds, accounts);
    }

    /// <summary>
    /// The ids of <paramref name="scopeKind"/> the caller may see: the union, over the permissions it
    /// holds that authorize this kind, of what its scoped assignments grant. One batched decision per
    /// permission, never one per node; anything not explicitly allowed stays hidden.
    /// </summary>
    private static async Task<HashSet<Guid>> AuthorizeScopeOptionIdsAsync(
        HttpContext context,
        IReadOnlyList<UserPermission> heldPermissions,
        AccessScopeKindDto scopeKind,
        IEnumerable<Guid> candidateIds,
        CancellationToken ct)
    {
        var requested = candidateIds.Where(static id => id != Guid.Empty).ToHashSet();
        var allowed = new HashSet<Guid>();
        foreach (var permission in heldPermissions)
        {
            if (!ScopeOptionPermissionCovers(permission, scopeKind))
            {
                continue;
            }

            var remaining = requested.Where(id => !allowed.Contains(id)).ToArray();
            if (remaining.Length == 0)
            {
                break;
            }

            var granted = await EndpointAuthorization
                .AuthorizeScopedManyAsync(context, permission, scopeKind, remaining, ct)
                .ConfigureAwait(false);
            allowed.UnionWith(granted.Where(requested.Contains));
        }

        return allowed;
    }

    /// <summary>Which node kinds each permission already authorizes a read of on an existing route.</summary>
    private static bool ScopeOptionPermissionCovers(UserPermission permission, AccessScopeKindDto scopeKind)
        => permission switch
        {
            UserPermission.ManageFundStructure => scopeKind is AccessScopeKindDto.LegalEntity or AccessScopeKindDto.Fund or AccessScopeKindDto.Account,
            // Fund-scoped direct-lending reads return the fund's accounts but never the fund record,
            // so a direct-lending grant discloses accounts only.
            UserPermission.ManageDirectLending => scopeKind is AccessScopeKindDto.Account,
            UserPermission.ViewTrades => scopeKind is AccessScopeKindDto.Account,
            UserPermission.ViewLedgerReports or UserPermission.ManageLedgerReports => scopeKind is AccessScopeKindDto.LegalEntity,
            _ => false
        };

    private static string ScopeOptionLabel(string? name, Guid id)
        => string.IsNullOrWhiteSpace(name) ? id.ToString("D") : name.Trim();
}
