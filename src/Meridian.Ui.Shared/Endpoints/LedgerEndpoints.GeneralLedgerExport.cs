using Meridian.Contracts.Api;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.SecurityMaster;
using Meridian.Identity.Auth;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class LedgerEndpoints
{
    private static void MapGeneralLedgerExportEndpoint(WebApplication app)
    {
        app.MapGet(UiApiRoutes.LedgerGeneralLedgerExport, async (
            Guid ledgerBookId, Guid periodId, Guid? closePriceSnapshotId, HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (!HasLedgerReadPermission(context) || !EndpointAuthorization.HasPermission(context, UserPermission.ExportData))
                return EndpointHelpers.Forbidden();
            var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            if (!HasAccountingPackageTenantScope(tenant))
                return EndpointHelpers.Forbidden();
            var store = ResolveJournalStore(context);
            var registry = context.RequestServices.GetService<IFundProfileTenancyRegistry>();
            // Use the same store for ownership lookup and snapshot capture, never a detached read source.
            if (store is not ILedgerReportingSnapshotSource source || registry is null)
                return ServiceUnavailable("A consistent ledger reporting snapshot and retained fund ownership are required.");
            try
            {
                var book = await store.GetLedgerBookAsync(ledgerBookId, context.RequestAborted).ConfigureAwait(false);
                if (book is null || book.LedgerBookId != ledgerBookId)
                    return EndpointHelpers.Forbidden();
                var owner = await registry.ResolveAsync(book.FundProfileId, context.RequestAborted).ConfigureAwait(false);
                if (owner is null || !string.Equals(owner.FundProfileId, book.FundProfileId, StringComparison.OrdinalIgnoreCase)
                    || !CloseWorkflowOwnerMatches(owner, tenant))
                    return EndpointHelpers.Forbidden();
                var query = new LedgerJournalEntryQuery(LedgerBookId: ledgerBookId, PeriodId: periodId);
                var snapshot = source is ILedgerOpenPositionReportingSnapshotSource positionSource
                    ? await positionSource.CaptureOpenPositionReportingSnapshotAsync(query, periodId, context.RequestAborted).ConfigureAwait(false)
                    : await source.CaptureReportingSnapshotAsync(query, periodId, context.RequestAborted).ConfigureAwait(false);
                if (snapshot.Period is null || snapshot.Period.PeriodId != periodId
                    || snapshot.Period.LedgerBookId != ledgerBookId)
                    return Results.NotFound();
                var canReadMarks = EndpointAuthorization.HasAnyPermission(context,
                    UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster);
                if (closePriceSnapshotId.HasValue && !canReadMarks)
                    return EndpointHelpers.Forbidden();
                var unrealized = canReadMarks
                    ? await ResolveGeneralLedgerMarksAsync(context, book, snapshot, closePriceSnapshotId).ConfigureAwait(false) : null;
                var package = await GeneralLedgerExportBuilder.BuildAsync(book, snapshot, context.RequestAborted, unrealized).ConfigureAwait(false);
                context.Response.Headers["X-Ledger-Snapshot-Sha256"] = package.SnapshotSha256;
                return Results.File(package.Bytes, "application/zip", $"general-ledger.v1-{periodId:D}.zip");
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or LedgerValidationException)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        })
        .WithName("ExportGeneralLedger")
        .RequireAuthenticatedSession()
        .RequireWorkstationTenantCompanyScope()
        .RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending,
            UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports)
        .Produces(StatusCodes.Status200OK, contentType: "application/zip")
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status501NotImplemented);
    }

    private static async Task<GeneralLedgerUnrealizedInputs?> ResolveGeneralLedgerMarksAsync(HttpContext context,
        LedgerBookRecord book, LedgerReportingSnapshot snapshot, Guid? requestedSnapshotId)
    {
        var pricing = ResolveClosePriceService(context);
        var securities = context.RequestServices.GetService<ISecurityMasterQueryService>();
        if (pricing is null || securities is null || snapshot.OpenPositions is null || snapshot.Period is null
            || book.FundStructureNodeKind != FundStructureNodeKindDto.Account)
            return null;
        var accountId = book.FundStructureNodeId.ToString("D");
        var bookId = book.LedgerBookId.ToString("D");
        var date = snapshot.Period.EndDate;
        var ct = context.RequestAborted;
        var prices = requestedSnapshotId is { } id
            ? await pricing.GetClosePriceSetAsync(accountId, bookId, date, id, ct).ConfigureAwait(false)
            : (await pricing.GetClosePriceSetsAsync(accountId, bookId, date, ct).ConfigureAwait(false))
                .OrderByDescending(set => set.Snapshot.Version).FirstOrDefault();
        // A newer unapproved correction does not authorize falling back to an older approval.
        if (prices?.Approval is null)
            return null;
        var details = new List<SecurityDetailDto>();
        foreach (var securityId in snapshot.OpenPositions.Lots.Select(lot => lot.SecurityId).Distinct())
        {
            var detail = await securities.GetByIdAsOfAsync(securityId, prices.Snapshot.EvaluatedAsOf, ct).ConfigureAwait(false);
            if (detail is not null)
                details.Add(detail);
        }
        return new(prices, details.AsReadOnly());
    }
}
