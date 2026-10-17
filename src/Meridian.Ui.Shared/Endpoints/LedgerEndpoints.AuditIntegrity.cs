using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Ledger;
using Meridian.Identity.Auth;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class LedgerEndpoints
{
    /// <summary>
    /// W9-GOV-008 criterion three verification tooling: verifies every retained accounting-action
    /// and ledger-event audit chain without appending. The chains are installation-wide structures
    /// rather than fund-scoped records, and the report carries only counts, classifications and
    /// chain positions -- no audit content -- so it is gated on maintenance authority rather than a
    /// fund tenant scope.
    /// </summary>
    private static void MapAuditIntegrityEndpoint(WebApplication app, JsonSerializerOptions jsonOptions)
    {
        app.MapGet(UiApiRoutes.LedgerAuditIntegrity, async (HttpContext context) =>
        {
            var services = context.RequestServices;
            var service = new AccountingAuditIntegrityService(
                services.GetService<IAccountingActionAuditStore>(),
                services.GetService<ILedgerJournalStore>() as ILedgerEventAuditVerifier,
                services.GetService<TimeProvider>());

            var report = await service.VerifyAsync(context.RequestAborted).ConfigureAwait(false);
            return Results.Json(report, jsonOptions);
        })
        .WithName("VerifyLedgerAuditIntegrity").RequirePermission(UserPermission.AdminMaintenance)
        .Produces<AccountingAuditIntegrityReportDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden);
    }
}
