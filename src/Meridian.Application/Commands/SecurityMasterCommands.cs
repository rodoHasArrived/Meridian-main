using Meridian.Application.ResultTypes;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Infrastructure.Adapters.Polygon;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;

namespace Meridian.Application.Commands;

/// <summary>
/// Handles --security-master-ingest CLI command for bulk-importing securities from CSV or JSON,
/// or directly from Polygon.io.
/// Usage:
///   --security-master-ingest ./securities.csv
///   --security-master-ingest ./securities.json
///   --security-master-ingest --provider polygon [--exchange XNAS] [--type CS]
///   --security-master-ingest --provider edgar [--scope all-filers] [--include-xbrl] [--include-filing-documents] [--cik CIK] [--max-filers N] [--dry-run]
/// Requires MERIDIAN_SECURITY_MASTER_CONNECTION_STRING to be configured.
/// </summary>
internal sealed class SecurityMasterCommands : ICliCommand
{
    // NOTE: _importService is null when the Security Master database is not configured at CLI
    // startup (e.g. the env var is absent). The full DI host wires the real service.
    private readonly ISecurityMasterImportService? _importService;
    private readonly ISecurityMasterService? _securityMasterService;
    private readonly IEdgarIngestOrchestrator? _edgarIngestOrchestrator;
    private readonly Serilog.ILogger _log;

    private const int ProgressReportInterval = 10;

    public SecurityMasterCommands(
        ISecurityMasterImportService? importService,
        Serilog.ILogger log,
        ISecurityMasterService? securityMasterService = null,
        IEdgarIngestOrchestrator? edgarIngestOrchestrator = null)
    {
        _importService = importService;
        _log = log;
        _securityMasterService = securityMasterService;
        _edgarIngestOrchestrator = edgarIngestOrchestrator;
    }

    public bool CanHandle(string[] args)
        => args.Any(a => a.Equals("--security-master-ingest", StringComparison.OrdinalIgnoreCase));

    public async Task<CliResult> ExecuteAsync(string[] args, CancellationToken ct = default)
    {
        var provider = CliArguments.GetValue(args, "--provider");
        if (string.Equals(provider, "polygon", StringComparison.OrdinalIgnoreCase))
            return await ExecutePolygonIngestAsync(args, ct).ConfigureAwait(false);
        if (string.Equals(provider, "edgar", StringComparison.OrdinalIgnoreCase))
            return await ExecuteEdgarIngestAsync(args, ct).ConfigureAwait(false);

        if (_importService is null)
        {
            Console.Error.WriteLine("Security Master is not configured.");
            Console.Error.WriteLine("Set MERIDIAN_SECURITY_MASTER_CONNECTION_STRING to use this command.");
            _log.Warning("--security-master-ingest invoked but Security Master is not configured");
            return CliResult.Fail(ErrorCode.ConfigurationInvalid);
        }

        // --- File-based ingest path ---
        return await ExecuteFileIngestAsync(args, ct).ConfigureAwait(false);
    }

    private async Task<CliResult> ExecuteEdgarIngestAsync(string[] args, CancellationToken ct)
    {
        if (_edgarIngestOrchestrator is null)
        {
            Console.Error.WriteLine("EDGAR ingest service is not available.");
            return CliResult.Fail(ErrorCode.ConfigurationInvalid);
        }

        var maxFilersValue = CliArguments.GetValue(args, "--max-filers");
        int? maxFilers = int.TryParse(maxFilersValue, out var parsedMaxFilers) && parsedMaxFilers > 0
            ? parsedMaxFilers
            : null;

        var request = new EdgarIngestRequest(
            Scope: CliArguments.GetValue(args, "--scope") ?? "all-filers",
            IncludeXbrl: args.Any(a => a.Equals("--include-xbrl", StringComparison.OrdinalIgnoreCase)),
            Cik: CliArguments.GetValue(args, "--cik"),
            MaxFilers: maxFilers,
            DryRun: args.Any(a => a.Equals("--dry-run", StringComparison.OrdinalIgnoreCase)),
            IncludeFilingDocuments: args.Any(a => a.Equals("--include-filing-documents", StringComparison.OrdinalIgnoreCase)));

        Console.WriteLine(
            $"Running EDGAR ingest (scope={request.Scope}, cik={request.Cik ?? "all"}, includeXbrl={request.IncludeXbrl}, includeFilingDocuments={request.IncludeFilingDocuments}, dryRun={request.DryRun})...");

        var result = await _edgarIngestOrchestrator.IngestAsync(request, ct).ConfigureAwait(false);

        Console.WriteLine();
        Console.WriteLine("EDGAR ingest complete:");
        Console.WriteLine($"  Filers processed       : {result.FilersProcessed}");
        Console.WriteLine($"  Ticker associations    : {result.TickerAssociationsStored}");
        Console.WriteLine($"  Fact partitions stored : {result.FactsStored}");
        Console.WriteLine($"  Security data stored   : {result.SecurityDataStored}");
        Console.WriteLine($"  Securities created     : {result.SecuritiesCreated}");
        Console.WriteLine($"  Securities amended     : {result.SecuritiesAmended}");
        Console.WriteLine($"  Securities skipped     : {result.SecuritiesSkipped}");
        Console.WriteLine($"  Conflicts detected     : {result.ConflictsDetected}");

        if (result.Errors.Count > 0)
        {
            Console.WriteLine($"  Errors ({result.Errors.Count}):");
            foreach (var error in result.Errors.Take(20))
                Console.WriteLine($"    - {error}");
            if (result.Errors.Count > 20)
                Console.WriteLine($"    ... and {result.Errors.Count - 20} more");
        }

        _log.Information(
            "EDGAR ingest completed: {Created} created, {Amended} amended, {Skipped} skipped, {Errors} errors",
            result.SecuritiesCreated,
            result.SecuritiesAmended,
            result.SecuritiesSkipped,
            result.Errors.Count);

        return result.Errors.Count == 0 ? CliResult.Ok() : CliResult.Fail(ErrorCode.ValidationFailed);
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

    private async Task<CliResult> ExecuteFileIngestAsync(string[] args, CancellationToken ct)
    {
        var filePath = CliArguments.GetValue(args, "--security-master-ingest");
        if (string.IsNullOrWhiteSpace(filePath))
        {
            Console.Error.WriteLine("Usage: --security-master-ingest <file.csv|file.json>");
            Console.Error.WriteLine("       --security-master-ingest --provider polygon [--exchange XNAS] [--type CS]");
            Console.Error.WriteLine("       --security-master-ingest --provider edgar [--scope all-filers] [--include-xbrl] [--include-filing-documents] [--cik CIK] [--max-filers N] [--dry-run]");
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
