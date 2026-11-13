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
    public static void MapLegacyIntercompanyAttributionEndpoints(WebApplication app, JsonSerializerOptions jsonOptions)
    {
        app.MapGet("/api/ledger/intercompany/legacy-attributions", async (Guid ledgerBookId, HttpContext context) =>
        {
            if (!await CanAccessAccountBindingBookAsync(context, ledgerBookId).ConfigureAwait(false)) return EndpointHelpers.Forbidden();
            var store = context.RequestServices.GetService<ILegacyIntercompanyAttributionStore>();
            if (store is null) return ServiceUnavailable();
            try { return Results.Json(await store.HistoryAsync(ledgerBookId, context.RequestAborted).ConfigureAwait(false), jsonOptions); }
            catch (TenantScopeRejectedException) { return EndpointHelpers.Forbidden(); }
        }).RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports)
            .RequireWorkstationTenantCompanyScope();

        app.MapGet("/api/ledger/intercompany/legacy-attributions/candidates", async (Guid ledgerBookId, HttpContext context) =>
        {
            if (!await CanAccessAccountBindingBookAsync(context, ledgerBookId).ConfigureAwait(false)) return EndpointHelpers.Forbidden();
            var journals = context.RequestServices.GetService<ILedgerJournalStore>();
            var bindings = context.RequestServices.GetService<IIntercompanyAccountBindingStore>();
            if (journals is null || bindings is null) return ServiceUnavailable();
            try
            {
                var book = await journals.GetLedgerBookAsync(ledgerBookId, context.RequestAborted).ConfigureAwait(false);
                var mappings = await bindings.HistoryAsync(ledgerBookId, context.RequestAborted).ConfigureAwait(false);
                var population = await journals.QueryAsync(new LedgerJournalEntryQuery(LedgerBookId: ledgerBookId), context.RequestAborted).ConfigureAwait(false);
                var candidates = population.SelectMany(record => record.Entry.Lines.Where(line => line.Dimensions?.AccountingArrangementId is null &&
                    mappings.Any(b => b.AccountName == line.Account.Name || b.HistoricalNames.Contains(line.Account.Name, StringComparer.Ordinal)))
                    .Select(line => new { ledgerBookId, record.Entry.JournalEntryId, line.EntryId,
                        SourceLineDigest = LegacyIntercompanyAttributionRules.SourceDigest(book!, record, line),
                        AccountName = line.Account.Name, AccountType = line.Account.AccountType.ToString(), line.Debit, line.Credit,
                        line.Dimensions, EffectiveDate = record.Entry.Metadata.EffectiveDate ?? DateOnly.FromDateTime(record.Entry.Timestamp.UtcDateTime) }));
                return Results.Json(candidates.ToArray(), jsonOptions);
            }
            catch (TenantScopeRejectedException) { return EndpointHelpers.Forbidden(); }
        }).RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageLedgerReports)
            .RequireAuthenticatedSession().RequireWorkstationTenantCompanyScope();

        app.MapPost("/api/ledger/intercompany/legacy-attributions", async (AppendLegacyIntercompanyAttributionRequest request, HttpContext context) =>
        {
            if (request.Attribution is null) return Results.BadRequest(new { error = "An attribution is required." });
            if (!TryResolveActor(context, out var actor) || !await CanAccessAccountBindingBookAsync(context, request.Attribution.LedgerBookId).ConfigureAwait(false))
                return EndpointHelpers.Forbidden();
            var store = context.RequestServices.GetService<ILegacyIntercompanyAttributionStore>();
            if (store is null) return ServiceUnavailable();
            var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            try
            {
                var previous = (await store.HistoryAsync(request.Attribution.LedgerBookId, context.RequestAborted).ConfigureAwait(false))
                    .SingleOrDefault(x => x.AttributionId == request.Attribution.AttributionId);
                var attribution = request.Attribution with { TenantId = tenant.TenantId!, CompanyId = string.Equals(tenant.CompanyId, "all", StringComparison.OrdinalIgnoreCase) ? null : tenant.CompanyId,
                    ApprovedBy = actor, ApprovedAt = previous?.ApprovedAt ?? DateTimeOffset.UtcNow };
                return Results.Json(await store.AppendAsync(attribution, request.ExpectedPreviousAttributionId,
                    context.RequestAborted).ConfigureAwait(false), jsonOptions);
            }
            catch (TenantScopeRejectedException) { return EndpointHelpers.Forbidden(); }
            catch (InvalidOperationException error) { return Results.Conflict(new { error = error.Message }); }
            catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
        }).RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageLedgerReports)
            .RequireAuthenticatedSession().RequireWorkstationTenantCompanyScope().RequireFundScopedWriteTenant()
            .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);
    }
}
