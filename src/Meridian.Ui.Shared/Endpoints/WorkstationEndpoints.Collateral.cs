using System.Globalization;
using System.Text;
using System.Text.Json;
using Meridian.Application.Monitoring;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Application.ProviderRouting;
using Meridian.DataIntegration.Monitoring;
using Meridian.Reporting;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Api;
using Meridian.Contracts.AssetOperations;
using Meridian.Identity.Auth;
using Meridian.Contracts.Configuration;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.StrategyEngine;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.Domain.Collectors;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Instruments.AssetOperations;
using Meridian.QuantScript.Compilation;
using Meridian.Storage.Export;
using Meridian.Storage.Interfaces;
using Meridian.Storage.Query;
using Meridian.Storage.Services;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Promotions;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Contracts.Reconciliation;
using Meridian.Ui.Shared.Services;
using Meridian.Ui.Shared.Workflows;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ContractSecurityMasterQueryService = Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService;
using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class WorkstationEndpoints
{
    /// <summary>
    /// Collateral ingest and exposure. Split out of the main map so the endpoint file stays under the
    /// no-new-god-file cap; the retention, tenant-partitioning and newest-wins rules these two routes
    /// depend on are documented on <see cref="Services.CollateralIngestionBuffer"/>.
    /// </summary>
    private static void MapCollateralEndpoints(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        group.MapPost(WorkstationSubroute(UiApiRoutes.WorkstationCollateralIngest), (
            IReadOnlyList<CollateralInputRow> rows,
            HttpContext context) =>
        {
            if (!HasOperationsContinuityMutationPermission(context))
            {
                return Results.Forbid();
            }

            // Before anything reads it. A body of `null` binds to a null list and an element of `null`
            // survives into the list, both of which are well-formed JSON the deserializer accepts --
            // so a malformed delivery would fault on the first dereference and answer 500, when the
            // whole point of validating here is that a producer gets told what to fix.
            if (rows is null)
            {
                return Results.BadRequest(new { error = "The request body must be an array of collateral rows." });
            }

            const int maxRowsPerRequest = 1_000;
            if (rows.Count > maxRowsPerRequest)
            {
                return Results.BadRequest(new { error = $"A maximum of {maxRowsPerRequest} collateral rows can be ingested per request." });
            }

            var buffer = context.RequestServices.GetService<CollateralIngestionBuffer>();
            if (buffer is null)
            {
                return Results.Accepted(value: new { ingested = 0, buffered = false });
            }

            // Validated before it can be retained. A non-consuming buffer keeps whatever it accepts, so
            // one malformed row poisons every later read for that tenant: a null counterparty survives
            // into BuildSnapshots, whose GroupBy key reaches ResolvePolicy and throws, and the row stays
            // until eviction. A future-dated AsOf is the same shape of problem -- newest-wins would make
            // it permanently authoritative and freeze that exposure.
            if (!TryValidateCollateralRows(rows, out var rejection))
            {
                return Results.BadRequest(new { error = rejection });
            }

            // One call, not a loop: a delivery replaces the exposures it restates, so ingesting row by
            // row would make the batch overwrite itself. Scoped to the tenant the server resolved,
            // never the payload -- the buffer is a singleton, so an unscoped write reaches every tenant.
            var outcome = buffer.IngestBatch(CollateralTenantScope.ForRequest(context), rows);
            if (outcome == CollateralIngestOutcome.ObservationExceedsWindow)
            {
                // The one thing the buffer cannot absorb. Observations are evicted whole, so an
                // observation past the window would delete itself and report that counterparty as
                // absent -- and answering 202 for the request that crossed the line is what would make
                // that silent. Refused instead, with what is already held left intact.
                return Results.BadRequest(new
                {
                    error = $"This delivery would leave one exposure holding more than the {CollateralIngestionBuffer.MaxBufferedRows:N0}-row retention window; nothing from it was retained. Narrow the exposure or report it as separate identities."
                });
            }

            return Results.Accepted(value: new { ingested = rows.Count, buffered = true });
        })
        .WithName("IngestCollateralRows").RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster)
        .Produces(202)
        .Produces(400)
        .Produces(403)
        .Produces(429)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationCollateralExposure), (HttpContext context) =>
        {
            if (!HasOperationsContinuityReadPermission(context))
            {
                return Results.Forbid();
            }

            var service = context.RequestServices.GetRequiredService<CollateralExposureService>();
            var buffer = context.RequestServices.GetService<CollateralIngestionBuffer>();
            var rows = buffer?.SnapshotCurrent(CollateralTenantScope.ForRequest(context)) ?? [];
            return Results.Json(BuildCollateralExposureSnapshot(service, rows), jsonOptions);
        })
        .WithName("GetWorkstationCollateralExposure").RequireAnyPermission(UserPermission.ViewDirectLending, UserPermission.ViewSecurityMaster, UserPermission.ManageDirectLending, UserPermission.ModifySecurityMaster, UserPermission.AdminMaintenance)
        .Produces<ExposureSnapshotDto>(200)
        .Produces(403);
    }

    /// <summary>
    /// Rejects a delivery whose rows cannot be retained safely. A non-consuming buffer keeps whatever
    /// it accepts, so one malformed row is not one bad response — it poisons every later read for that
    /// tenant until eviction.
    /// <para>
    /// Blank identity fields: <c>BuildSnapshots</c> groups by counterparty and hands the group key to
    /// <c>ResolvePolicy</c>, whose dictionary lookup throws on null, so a single null-counterparty row
    /// turns the tenant's exposure endpoint into a persistent 500.
    /// </para>
    /// <para>
    /// Future-dated observations: the buffer resolves restatements by newest-<c>AsOf</c>-wins, so a
    /// far-future timestamp makes that exposure permanently authoritative and freezes its coverage and
    /// breach state against every legitimate update that follows.
    /// </para>
    /// </summary>
    private static bool TryValidateCollateralRows(IReadOnlyList<CollateralInputRow> rows, out string rejection)
    {
        var horizon = DateTimeOffset.UtcNow.Add(MaxCollateralClockSkew);
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            if (row is null)
            {
                rejection = $"Row {index} is null.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(row.Counterparty) ||
                string.IsNullOrWhiteSpace(row.ProductType) ||
                string.IsNullOrWhiteSpace(row.CollateralType))
            {
                rejection = $"Row {index} is missing counterparty, product type, or collateral type.";
                return false;
            }

            // Length, not just presence. The row cap bounds how many rows are retained, not how many
            // bytes: a producer sending megabyte identity strings can leave 20,000 of them held per
            // tenant, and each one is then re-walked on every read to be trimmed, lowercased, hashed
            // into the exposure key, grouped and projected. Bounding the field is what makes the row
            // cap a memory bound rather than only a row count.
            if (ExceedsIdentityLength(row.Counterparty) ||
                ExceedsIdentityLength(row.ProductType) ||
                ExceedsIdentityLength(row.CollateralType) ||
                ExceedsIdentityLength(row.ChunkId))
            {
                rejection = $"Row {index} carries an identity field longer than {MaxCollateralIdentityLength} characters.";
                return false;
            }

            if (row.AsOf > horizon)
            {
                rejection = $"Row {index} is dated beyond the permitted {MaxCollateralClockSkew.TotalMinutes:F0}-minute clock skew.";
                return false;
            }

            if (Exceeds(row.PositionNotional) ||
                Exceeds(row.MarkToMarket) ||
                Exceeds(row.CollateralBalance) ||
                Exceeds(row.InitialMargin) ||
                Exceeds(row.VariationMargin))
            {
                rejection = $"Row {index} carries a financial value beyond the permitted magnitude of {MaxCollateralValueMagnitude:E0}.";
                return false;
            }
        }

        rejection = string.Empty;
        return true;

        static bool Exceeds(decimal value) => Math.Abs(value) > MaxCollateralValueMagnitude;
    }

    /// <summary>
    /// The longest an identity field on a collateral row may be. Counterparty names, product types,
    /// collateral types and producer chunk ids are all short by nature; the bound exists so the
    /// buffer's row cap bounds retained bytes as well as retained rows.
    /// </summary>
    private const int MaxCollateralIdentityLength = 256;

    private static bool ExceedsIdentityLength(string? value) => value is not null && value.Length > MaxCollateralIdentityLength;

    // Wide enough for ordinary producer clock drift, narrow enough that a misconfigured clock cannot
    // pin an exposure indefinitely.
    private static readonly TimeSpan MaxCollateralClockSkew = TimeSpan.FromMinutes(15);

    /// <summary>
    /// The largest magnitude any single financial field on a collateral row may carry.
    /// <para>
    /// Chosen so <c>BuildSnapshots</c> cannot overflow rather than to express a business limit -- a
    /// sextillion is far above any real exposure, so a row past it is a producer defect, and the
    /// non-consuming buffer would keep it. Its widest aggregate is the coverage comparison
    /// <c>required * 999</c>, where <c>required</c> sums two magnitudes per row across the buffer's
    /// 20,000-row cap: 2 x 20,000 x 1e21 x 999 is about 4e28, inside <see cref="decimal.MaxValue"/>
    /// (about 7.9e28). Every other sum in that method is strictly smaller.
    /// </para>
    /// </summary>
    private const decimal MaxCollateralValueMagnitude = 1_000_000_000_000_000_000_000m;
}
