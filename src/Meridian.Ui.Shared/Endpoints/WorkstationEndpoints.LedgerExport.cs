using System.IO.Compression;
using Meridian.Contracts.Api;
using Meridian.Identity.Auth;
using Meridian.Storage.Export;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class WorkstationEndpoints
{
    private static void MapRunLedgerExportEndpoint(RouteGroupBuilder group)
    {
        group.MapGet(WorkstationSubroute(UiApiRoutes.RunsLedgerExport), async (
            string runId, string? format, DateTimeOffset? asOf, HttpContext context) =>
        {
            if (!Enum.TryParse<ExportFormat>(format ?? "Csv", ignoreCase: true, out var exportFormat) ||
                exportFormat is not (ExportFormat.Csv or ExportFormat.Xlsx or ExportFormat.Parquet or ExportFormat.Arrow))
                return Results.BadRequest(new { error = "Choose Csv, Xlsx, Parquet or Arrow." });

            var readService = context.RequestServices.GetRequiredService<StrategyRunReadService>();
            var result = await readService.GetRetainedResultAsync(runId, ResolveStrategyRunReadScope(context), context.RequestAborted).ConfigureAwait(false);
            if (result is null)
                return Results.NotFound();
            var cut = asOf ?? result.Snapshots.LastOrDefault()?.Timestamp ?? result.Ledger.Journal.LastOrDefault()?.Timestamp ?? DateTimeOffset.MinValue;
            var firstPosting = result.Ledger.Journal.FirstOrDefault()?.Timestamp ?? cut;
            if (cut < firstPosting)
                return Results.BadRequest(new { error = "The export date precedes this run's first posting." });

            var staging = Path.Combine(Path.GetTempPath(), $"meridian-ledger-export-{Guid.NewGuid():N}");
            try
            {
                var exporter = new AnalysisExportService(staging);
                var periodStart = firstPosting == DateTimeOffset.MinValue ? firstPosting : firstPosting.AddTicks(-1);
                LedgerExportRequest request;
                try
                {
                    request = BacktestLedgerExportMapper.Build(result, periodStart, cut, staging, exportFormat);
                }
                catch (LedgerExportEvidenceUnavailableException)
                {
                    // Journal-only legacy runs and dates without a complete valuation cut still
                    // export the journal, trial balance and cash flows. The package records that
                    // lot schedules are unavailable; missing evidence must never imply zero lots.
                    request = new LedgerExportRequest(result.Ledger, periodStart, cut, staging, exportFormat);
                }
                catch (InvalidOperationException)
                {
                    return Results.Problem("Retained lot evidence failed accounting-package integrity validation. Review the run's lot records before exporting.", statusCode: StatusCodes.Status409Conflict);
                }
                var exported = await exporter.ExportLedgerAsync(request, context.RequestAborted).ConfigureAwait(false);
                using var bytes = new MemoryStream();
                using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
                    foreach (var path in Directory.EnumerateFiles(exported.OutputDirectory))
                        zip.CreateEntryFromFile(path, Path.GetFileName(path));
                return Results.File(bytes.ToArray(), "application/zip", $"ledger-{cut:yyyyMMdd}-{exportFormat.ToString().ToLowerInvariant()}.zip");
            }
            finally
            {
                if (Directory.Exists(staging))
                    Directory.Delete(staging, recursive: true);
            }
        })
        .WithName("ExportRunLedger").RequireAnyPermission(UserPermission.ViewStrategies, UserPermission.ManageStrategies)
        .Produces(200, contentType: "application/zip")
        .Produces(400).Produces(404).Produces(409)
        .AddEndpointFilter(RequireStrategyRunReadAccessAsync);

    }
}
