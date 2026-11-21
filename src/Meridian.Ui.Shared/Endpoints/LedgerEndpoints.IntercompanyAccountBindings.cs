using System.Text.Json;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Identity.Auth;
using Meridian.Storage.Ledger;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class LedgerEndpoints
{
    public static void MapIntercompanyAccountBindingEndpoints(WebApplication app, JsonSerializerOptions jsonOptions)
    {
        app.MapGet("/api/ledger/intercompany/account-bindings", async (
            Guid ledgerBookId, DateOnly asOf, HttpContext context) =>
        {
            var store = context.RequestServices.GetService<IIntercompanyAccountBindingStore>();
            if (store is null)
                return ServiceUnavailable();
            if (!await CanAccessAccountBindingBookAsync(context, ledgerBookId).ConfigureAwait(false))
                return EndpointHelpers.Forbidden();
            try
            {
                return Results.Json(await store.ListAsync(ledgerBookId, asOf, context.RequestAborted).ConfigureAwait(false), jsonOptions);
            }
            catch (TenantScopeRejectedException) { return EndpointHelpers.Forbidden(); }
        })
        .WithName("ListIntercompanyAccountBindings")
        .RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports)
        .RequireWorkstationTenantCompanyScope()
        .Produces<IReadOnlyList<IntercompanyAccountBindingDto>>();

        app.MapGet("/api/ledger/intercompany/account-bindings/history", async (
            Guid ledgerBookId, HttpContext context) =>
        {
            var store = context.RequestServices.GetService<IIntercompanyAccountBindingStore>();
            if (store is null)
                return ServiceUnavailable();
            if (!await CanAccessAccountBindingBookAsync(context, ledgerBookId).ConfigureAwait(false))
                return EndpointHelpers.Forbidden();
            try
            {
                return Results.Json(await store.HistoryAsync(ledgerBookId, context.RequestAborted).ConfigureAwait(false), jsonOptions);
            }
            catch (TenantScopeRejectedException) { return EndpointHelpers.Forbidden(); }
        })
        .WithName("ListIntercompanyAccountBindingHistory")
        .RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports)
        .RequireWorkstationTenantCompanyScope()
        .Produces<IReadOnlyList<IntercompanyAccountBindingDto>>();

        app.MapPost("/api/ledger/intercompany/account-bindings", async (
            AppendIntercompanyAccountBindingRequest request, HttpContext context) =>
        {
            if (!TryResolveActor(context, out var actor))
                return EndpointHelpers.Forbidden();
            var store = context.RequestServices.GetService<IIntercompanyAccountBindingStore>();
            var configuration = context.RequestServices.GetService<IAccountingConfigurationService>();
            var journals = context.RequestServices.GetService<ILedgerJournalStore>();
            if (store is null || configuration is null || journals is null)
                return ServiceUnavailable();
            if (!await CanAccessAccountBindingBookAsync(context, request.LedgerBookId).ConfigureAwait(false))
                return EndpointHelpers.Forbidden();
            if (request.HistoricalNames is null)
                return Results.BadRequest(new { error = "Historical names must be supplied explicitly, including an empty list for an initial mapping." });
            var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            try
            {
                var existing = await store.GetRevisionAsync(request.RevisionId, context.RequestAborted).ConfigureAwait(false);
                if (existing is not null)
                {
                    var sameCommand = existing.BindingId == request.BindingId && existing.LedgerBookId == request.LedgerBookId
                        && existing.ChartAccountNodeId == request.ChartAccountNodeId && existing.RecordedBy == actor
                        && existing.EffectiveFrom == request.EffectiveFrom && existing.EffectiveTo == request.EffectiveTo
                        && existing.Reason == request.Reason && existing.IsArchived == request.IsArchived
                        && existing.PreviousRevisionId == request.ExpectedPreviousRevisionId
                        && existing.HistoricalNames.SequenceEqual(request.HistoricalNames);
                    return sameCommand ? Results.Json(existing, jsonOptions)
                        : Results.Conflict(new { error = "The revision identity already belongs to a different account-binding command." });
                }
                var book = await journals.GetLedgerBookAsync(request.LedgerBookId, context.RequestAborted).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("The ledger book is unavailable.");
                var workspace = await configuration.GetWorkspaceAsync(book.FundProfileId, request.LedgerBookId,
                    context.RequestAborted, tenant.TenantId, tenant.CompanyId).ConfigureAwait(false);
                var account = workspace.ChartOfAccounts.SingleOrDefault(x => x.NodeId == request.ChartAccountNodeId)
                    ?? throw new InvalidOperationException("The stable chart account is missing or ambiguous.");
                if (account.IntercompanyRole is not { } role)
                    throw new InvalidOperationException("Assign the intercompany role in the chart before retaining its account binding.");
                var previous = (await store.HistoryAsync(request.LedgerBookId, context.RequestAborted).ConfigureAwait(false))
                    .Where(x => x.BindingId == request.BindingId).OrderByDescending(x => x.RevisionNumber).FirstOrDefault();
                var revision = new IntercompanyAccountBindingDto(request.BindingId, request.RevisionId,
                    (previous?.RevisionNumber ?? 0) + 1, request.LedgerBookId, request.ChartAccountNodeId,
                    role, account.Path, account.AccountName, account.AccountType, request.EffectiveFrom, request.EffectiveTo,
                    request.HistoricalNames, tenant.TenantId!, tenant.CompanyId, actor, DateTimeOffset.UtcNow,
                    request.Reason, request.ExpectedPreviousRevisionId, request.IsArchived);
                return Results.Json(await store.AppendAsync(revision, request.ExpectedPreviousRevisionId,
                    context.RequestAborted).ConfigureAwait(false), jsonOptions);
            }
            catch (TenantScopeRejectedException) { return EndpointHelpers.Forbidden(); }
            catch (ArgumentException exception) { return Results.BadRequest(new { error = exception.Message }); }
            catch (InvalidOperationException exception) { return Results.Conflict(new { error = exception.Message }); }
        })
        .WithName("AppendIntercompanyAccountBinding")
        .RequireAuthenticatedSession()
        .RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageLedgerReports)
        .RequireWorkstationTenantCompanyScope().RequireFundScopedWriteTenant()
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .Produces<IntercompanyAccountBindingDto>();
    }

    private static async Task<bool> CanAccessAccountBindingBookAsync(HttpContext context, Guid bookId)
    {
        var journals = context.RequestServices.GetService<ILedgerJournalStore>();
        var registry = context.RequestServices.GetService<IFundProfileTenancyRegistry>();
        if (journals is null || registry is null)
            return false;
        var book = await journals.GetLedgerBookAsync(bookId, context.RequestAborted).ConfigureAwait(false);
        var owner = book is null ? null : await registry.ResolveAsync(book.FundProfileId, context.RequestAborted).ConfigureAwait(false);
        return owner is not null && CloseWorkflowOwnerMatches(owner, HttpContextWorkstationTenantContextAccessor.Resolve(context));
    }
}
