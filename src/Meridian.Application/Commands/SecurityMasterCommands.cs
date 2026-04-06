using Meridian.Application.ResultTypes;
using Meridian.Application.SecurityMaster;
using Meridian.Infrastructure.Adapters.Edgar;
using Meridian.Infrastructure.Adapters.Polygon;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;

namespace Meridian.Application.Commands;

/// <summary>
/// Handles --security-master-ingest CLI command for bulk-importing securities from CSV or JSON,
/// or directly from Polygon.io or EDGAR.
/// Usage:
///   --security-master-ingest ./securities.csv
///   --security-master-ingest ./securities.json
///   --security-master-ingest --provider polygon [--exchange XNAS] [--type CS]
///   --security-master-ingest --provider edgar [--enrich]
/// Requires MERIDIAN_SECURITY_MASTER_CONNECTION_STRING to be configured.
/// </summary>
internal sealed class SecurityMasterCommands : ICliCommand
{
    // NOTE: _importService is null when the Security Master database is not configured at CLI
    // startup (e.g. the env var is absent). The full DI host wires the real service.
    private readonly ISecurityMasterImportService? _importService;
    private readonly ISecurityMasterService? _securityMasterService;
    private readonly Serilog.ILogger _log;

    private const int ProgressReportInterval = 10;

    public SecurityMasterCommands(
        ISecurityMasterImportService? importService,
        Serilog.ILogger log,
        ISecurityMasterService? securityMasterService = null)
    {
        _importService = importService;
        _log = log;
        _securityMasterService = securityMasterService;
    }

    public bool CanHandle(string[] args)
        => args.Any(a => a.Equals("--security-master-ingest", StringComparison.OrdinalIgnoreCase));

    public async Task<CliResult> ExecuteAsync(string[] args, CancellationToken ct = default)
    {
        if (_importService is null)
        {
            Console.Error.WriteLine("Security Master is not configured.");
            Console.Error.WriteLine("Set MERIDIAN_SECURITY_MASTER_CONNECTION_STRING to use this command.");
            _log.Warning("--security-master-ingest invoked but Security Master is not configured");
            return CliResult.Fail(ErrorCode.ConfigurationInvalid);
        }

        // --- Polygon provider ingest path ---
        var provider = CliArguments.GetValue(args, "--provider");
        if (string.Equals(provider, "polygon", StringComparison.OrdinalIgnoreCase))
            return await ExecutePolygonIngestAsync(args, ct).ConfigureAwait(false);

        // --- EDGAR provider ingest path ---
        if (string.Equals(provider, "edgar", StringComparison.OrdinalIgnoreCase))
            return await ExecuteEdgarIngestAsync(args, ct).ConfigureAwait(false);

        // --- File-based ingest path ---
        return await ExecuteFileIngestAsync(args, ct).ConfigureAwait(false);
    }

    private async Task<CliResult> ExecutePolygonIngestAsync(string[] args, CancellationToken ct)
    {
        if (_securityMasterService is null)
        {
            Console.Error.WriteLine("Security Master service is not available for provider ingest.");
            return CliResult.Fail(ErrorCode.ConfigurationInvalid);
        }

        var exchange = CliArguments.GetValue(args, "--exchange");
        var assetType = CliArguments.GetValue(args, "--type");

        _log.Information(
            "Starting Polygon Security Master ingest (exchange={Exchange}, type={Type})",
            exchange ?? "(all)", assetType ?? "(all)");
        Console.WriteLine($"Fetching tickers from Polygon.io (exchange={exchange ?? "all"}, type={assetType ?? "all"})...");

        IReadOnlyList<Contracts.SecurityMaster.CreateSecurityRequest> requests;
        using var ingestProvider = new PolygonSecurityMasterIngestProvider(
            NullLogger<PolygonSecurityMasterIngestProvider>.Instance);

        var fetchProgress = new Progress<int>(count =>
        {
            if (count % 500 == 0)
                Console.WriteLine($"  Fetched {count} tickers...");
        });

        requests = await ingestProvider.FetchAllAsync(exchange, assetType, fetchProgress, ct)
            .ConfigureAwait(false);

        if (requests.Count == 0)
        {
            Console.WriteLine("No tickers returned from Polygon.");
            return CliResult.Ok();
        }

        Console.WriteLine($"Fetched {requests.Count} tickers. Importing into Security Master...");

        int imported = 0, skipped = 0, failed = 0;
        var errors = new List<string>();

        for (int i = 0; i < requests.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var request = requests[i];
            try
            {
                await _securityMasterService.CreateAsync(request, ct).ConfigureAwait(false);
                imported++;
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase) ||
                    ex.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase))
                {
                    skipped++;
                }
                else
                {
                    failed++;
                    var ticker = request.Identifiers.FirstOrDefault()?.Value ?? "?";
                    errors.Add($"{ticker}: {ex.Message}");
                }
            }

            if ((i + 1) % ProgressReportInterval == 0 || i == requests.Count - 1)
                Console.WriteLine($"  Progress: {i + 1}/{requests.Count} ({imported} imported, {failed} failed, {skipped} skipped)");
        }

        PrintSummary(imported, skipped, failed, 0, errors);
        _log.Information("Polygon ingest: {Imported} imported, {Skipped} skipped, {Failed} failed",
            imported, skipped, failed);

        return failed == 0 ? CliResult.Ok() : CliResult.Fail(ErrorCode.ValidationFailed);
    }

    private async Task<CliResult> ExecuteEdgarIngestAsync(string[] args, CancellationToken ct)
    {
        if (_securityMasterService is null)
        {
            Console.Error.WriteLine("Security Master service is not available for provider ingest.");
            return CliResult.Fail(ErrorCode.ConfigurationInvalid);
        }

        var enrich = args.Any(a => a.Equals("--enrich", StringComparison.OrdinalIgnoreCase));

        _log.Information("Starting EDGAR Security Master ingest (enrich={Enrich})", enrich);
        Console.WriteLine($"Fetching all SEC-reporting companies from EDGAR (enrich={enrich})...");
        if (enrich)
            Console.WriteLine("  Note: enrichment mode fetches one extra request per company and is slow.");

        IReadOnlyList<Contracts.SecurityMaster.CreateSecurityRequest> requests;
        using var ingestProvider = new EdgarSecurityMasterIngestProvider(
            NullLogger<EdgarSecurityMasterIngestProvider>.Instance);

        var fetchProgress = new Progress<int>(count =>
        {
            if (count % 500 == 0)
                Console.WriteLine($"  Processed {count} companies...");
        });

        requests = await ingestProvider.FetchAllAsync(enrich, fetchProgress, ct).ConfigureAwait(false);

        if (requests.Count == 0)
        {
            Console.WriteLine("No companies returned from EDGAR.");
            return CliResult.Ok();
        }

        Console.WriteLine($"Fetched {requests.Count} companies. Importing into Security Master...");

        int imported = 0, skipped = 0, failed = 0;
        var errors = new List<string>();

        for (int i = 0; i < requests.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var request = requests[i];
            try
            {
                await _securityMasterService.CreateAsync(request, ct).ConfigureAwait(false);
                imported++;
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase) ||
                    ex.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase))
                {
                    skipped++;
                }
                else
                {
                    failed++;
                    var ticker = request.Identifiers.FirstOrDefault()?.Value ?? "?";
                    errors.Add($"{ticker}: {ex.Message}");
                }
            }

            if ((i + 1) % ProgressReportInterval == 0 || i == requests.Count - 1)
                Console.WriteLine($"  Progress: {i + 1}/{requests.Count} ({imported} imported, {failed} failed, {skipped} skipped)");
        }

        PrintSummary(imported, skipped, failed, 0, errors);
        _log.Information("EDGAR ingest: {Imported} imported, {Skipped} skipped, {Failed} failed",
            imported, skipped, failed);

        return failed == 0 ? CliResult.Ok() : CliResult.Fail(ErrorCode.ValidationFailed);
    }

    private async Task<CliResult> ExecuteFileIngestAsync(string[] args, CancellationToken ct)
    {
        var filePath = CliArguments.GetValue(args, "--security-master-ingest");
        if (string.IsNullOrWhiteSpace(filePath))
        {
            Console.Error.WriteLine("Usage: --security-master-ingest <file.csv|file.json>");
            Console.Error.WriteLine("       --security-master-ingest --provider polygon [--exchange XNAS] [--type CS]");
            Console.Error.WriteLine("       --security-master-ingest --provider edgar [--enrich]");
            return CliResult.Fail(ErrorCode.RequiredFieldMissing);
        }

        if (!File.Exists(filePath))
        {
            Console.Error.WriteLine($"File not found: {filePath}");
            return CliResult.Fail(ErrorCode.FileNotFound);
        }

        var extension = Path.GetExtension(filePath);
        if (!extension.Equals(".csv", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"Unsupported file format: {extension}. Only .csv and .json are supported.");
            return CliResult.Fail(ErrorCode.ValidationFailed);
        }

        _log.Information("Starting Security Master ingest from {File}", filePath);
        Console.WriteLine($"Importing securities from {filePath}...");

        string content;
        try
        {
            content = await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"Error reading file: {ex.Message}");
            return CliResult.Fail(ErrorCode.StorageError);
        }

        var progress = new Progress<SecurityMasterImportProgress>(p =>
        {
            if (p.Processed % ProgressReportInterval == 0 || p.Processed == p.Total)
                Console.WriteLine($"  Progress: {p.Processed}/{p.Total} ({p.Imported} imported, {p.Failed} failed)");
        });

        var result = await _importService!.ImportAsync(content, extension, progress, ct)
            .ConfigureAwait(false);

        Console.WriteLine();
        PrintSummary(result.Imported, result.Skipped, result.Failed, result.ConflictsDetected, result.Errors);
        _log.Information(
            "Security Master ingest completed: {Imported} imported, {Skipped} skipped, {Failed} failed",
            result.Imported, result.Skipped, result.Failed);

        return result.Failed == 0 ? CliResult.Ok() : CliResult.Fail(ErrorCode.ValidationFailed);
    }

    private static void PrintSummary(int imported, int skipped, int failed, int conflictsDetected, IReadOnlyList<string> errors)
    {
        Console.WriteLine();
        Console.WriteLine("Import complete:");
        Console.WriteLine($"  Imported  : {imported}");
        Console.WriteLine($"  Skipped   : {skipped}");
        Console.WriteLine($"  Failed    : {failed}");
        Console.WriteLine($"  Conflicts : {conflictsDetected}");

        if (errors.Count > 0)
        {
            Console.WriteLine($"  Errors ({errors.Count}):");
            foreach (var error in errors.Take(20))
                Console.WriteLine($"    - {error}");
            if (errors.Count > 20)
                Console.WriteLine($"    ... and {errors.Count - 20} more");
        }
    }
}
