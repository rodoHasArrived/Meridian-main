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
    /// <remarks>
    /// Verification scans every chained row while holding each chain's head row lock, the same lock
    /// ledger and accounting appends take, so concurrent runs would queue behind one another and
    /// hold up posting for the whole time. One run at a time is admitted per host; a request that
    /// arrives while one is running is refused with 409 rather than queued. The route deliberately
    /// carries no shared rate-limit policy: as a GET it is authorized by its endpoint filter, which
    /// runs after rate-limit admission, so a shared partition would let unauthorized callers drain
    /// the per-address permits other mutations depend on. Only an authorized caller reaches the gate,
    /// and the gate alone bounds the load.
    /// </remarks>
    private static void MapAuditIntegrityEndpoint(WebApplication app, JsonSerializerOptions jsonOptions)
    {
        app.MapGet(UiApiRoutes.LedgerAuditIntegrity, async (HttpContext context) =>
        {
            // Each request is a fresh verification; no browser or intermediary may answer it from
            // a stored earlier report.
            context.Response.Headers.CacheControl = "no-store";

            if (!await AuditIntegrityGate.WaitAsync(0, context.RequestAborted).ConfigureAwait(false))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Audit chain verification already running",
                    detail: "Another audit chain verification is in progress on this host. Retry when it completes.");
            }

            try
            {
                var services = context.RequestServices;
                var service = new AccountingAuditIntegrityService(
                    services.GetService<IAccountingActionAuditStore>(),
                    services.GetService<ILedgerJournalStore>() as ILedgerEventAuditVerifier,
                    services.GetService<TimeProvider>());

                var report = await service.VerifyAsync(context.RequestAborted).ConfigureAwait(false);
                return Results.Json(report, jsonOptions);
            }
            finally
            {
                AuditIntegrityGate.Release();
            }
        })
        .WithName("VerifyLedgerAuditIntegrity").RequirePermission(UserPermission.AdminMaintenance)
        .Produces<AccountingAuditIntegrityReportDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status409Conflict);
    }

    private static readonly SemaphoreSlim AuditIntegrityGate = new(1, 1);
}
