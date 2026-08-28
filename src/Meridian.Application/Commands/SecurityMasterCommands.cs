using Meridian.Platform.Results;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Infrastructure.Adapters.Polygon;
using Meridian.Storage.SecurityMaster;
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
///   --security-master-ingest --provider corporate-actions [--symbols AAPL,MSFT] [--minimum-sources N] [--dry-run]
///   --security-master-normalize-corporate-actions [--apply]
/// Requires MERIDIAN_SECURITY_MASTER_CONNECTION_STRING to be configured.
/// </summary>
internal sealed class SecurityMasterCommands : ICliCommand
{
    // NOTE: _importService is null when the Security Master database is not configured at CLI
    // startup (e.g. the env var is absent). The full DI host wires the real service.
    private readonly ISecurityMasterImportService? _importService;
    private readonly ISecurityMasterService? _securityMasterService;
    private readonly IEdgarIngestOrchestrator? _edgarIngestOrchestrator;
    private readonly CorporateActionIngestOrchestrator? _corporateActionIngestOrchestrator;
    private readonly ISecurityMasterEventStore? _securityMasterEventStore;
    private readonly Serilog.ILogger _log;

    private const int ProgressReportInterval = 10;

    public SecurityMasterCommands(
        ISecurityMasterImportService? importService,
        Serilog.ILogger log,
        ISecurityMasterService? securityMasterService = null,
        IEdgarIngestOrchestrator? edgarIngestOrchestrator = null,
        CorporateActionIngestOrchestrator? corporateActionIngestOrchestrator = null,
        ISecurityMasterEventStore? securityMasterEventStore = null)
    {
        _importService = importService;
        _log = log;
        _securityMasterService = securityMasterService;
        _edgarIngestOrchestrator = edgarIngestOrchestrator;
        _corporateActionIngestOrchestrator = corporateActionIngestOrchestrator;
        _securityMasterEventStore = securityMasterEventStore;
    }

    public IReadOnlyList<string> Triggers { get; } = ["--security-master-ingest", "--security-master-normalize-corporate-actions"];

    public bool CanHandle(string[] args) => CliArguments.MatchesAnyFlag(args, Triggers);

    public async Task<CliResult> ExecuteAsync(string[] args, CancellationToken ct = default)
    {
        if (args.Any(a => a.Equals("--security-master-normalize-corporate-actions", StringComparison.OrdinalIgnoreCase)))
            return await ExecuteCorporateActionNormalizationAsync(args, ct).ConfigureAwait(false);

        var provider = CliArguments.GetValue(args, "--provider");
        if (string.Equals(provider, "polygon", StringComparison.OrdinalIgnoreCase))
            return await ExecutePolygonIngestAsync(args, ct).ConfigureAwait(false);
        if (string.Equals(provider, "edgar", StringComparison.OrdinalIgnoreCase))
            return await ExecuteEdgarIngestAsync(args, ct).ConfigureAwait(false);
        if (string.Equals(provider, "corporate-actions", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(provider, "corporate-action", StringComparison.OrdinalIgnoreCase))
        {
            return await ExecuteCorporateActionIngestAsync(args, ct).ConfigureAwait(false);
        }

        // --- File-based ingest path ---
        return await ExecuteFileIngestAsync(args, ct).ConfigureAwait(false);
    }

    private async Task<CliResult> ExecuteCorporateActionNormalizationAsync(string[] args, CancellationToken ct)
    {
        if (_securityMasterEventStore is null)
        {
            Console.Error.WriteLine("Security Master event store is not available.");
            Console.Error.WriteLine("Set MERIDIAN_SECURITY_MASTER_CONNECTION_STRING to use this command.");
            return CliResult.Fail(ErrorCode.ConfigurationInvalid);
        }

        var apply = args.Any(a => a.Equals("--apply", StringComparison.OrdinalIgnoreCase));
        Console.WriteLine(apply
            ? "Normalizing stored corporate-action event types (applying rewrites)..."
            : "Normalizing stored corporate-action event types (dry run; pass --apply to rewrite)...");

        var result = await _securityMasterEventStore
            .NormalizeCorporateActionEventTypesAsync(apply, ct)
            .ConfigureAwait(false);

        Console.WriteLine();
        if (result.Renames.Count == 0)
        {
            Console.WriteLine("All stored corporate-action event types are already canonical.");
        }
        else
        {
            Console.WriteLine(result.Applied ? "Rewrites applied:" : "Planned rewrites:");
            foreach (var rename in result.Renames)
                Console.WriteLine($"  '{rename.StoredValue}' -> '{rename.CanonicalName}' ({rename.RowCount} row(s))");
        }

        if (result.UnmappedValues.Count > 0)
        {
            Console.WriteLine("Values left untouched (no canonical mapping — extend the descriptor catalog aliases first):");
            foreach (var unmapped in result.UnmappedValues)
                Console.WriteLine($"  '{unmapped.StoredValue}' ({unmapped.RowCount} row(s))");
        }

        _log.Information(
            "Corporate-action event-type normalization: {RenameCount} rename(s), {UnmappedCount} unmapped value(s), applied={Applied}",
            result.Renames.Count,
            result.UnmappedValues.Count,
            result.Applied);

        return CliResult.Ok();
    }

    private async Task<CliResult> ExecuteCorporateActionIngestAsync(string[] args, CancellationToken ct)
    {
        if (_corporateActionIngestOrchestrator is null)
        {
            Console.Error.WriteLine("Corporate action ingest service is not available.");
            return CliResult.Fail(ErrorCode.ConfigurationInvalid);
        }

        var minimumSourcesValue = CliArguments.GetValue(args, "--minimum-sources")
            ?? CliArguments.GetValue(args, "--min-sources");
        var minimumSources = int.TryParse(minimumSourcesValue, out var parsedMinimumSources) && parsedMinimumSources > 0
            ? parsedMinimumSources
            : 1;
        var symbols = ParseSymbols(args);
        var request = new CorporateActionIngestRequest(
            Symbols: symbols.Count == 0 ? null : symbols,
            DryRun: args.Any(a => a.Equals("--dry-run", StringComparison.OrdinalIgnoreCase)),
            MinimumSourcesToApply: minimumSources,
            Actor: "meridian-cli");

        Console.WriteLine(
            $"Running corporate-action ingest (symbols={(symbols.Count == 0 ? "all mastered tickers" : string.Join(",", symbols))}, minimumSources={minimumSources}, dryRun={request.DryRun})...");

        var result = await _corporateActionIngestOrchestrator.IngestAsync(request, ct).ConfigureAwait(false);

        Console.WriteLine();
        Console.WriteLine("Corporate-action ingest complete:");
        Console.WriteLine($"  Securities scanned  : {result.SecuritiesScanned}");
        Console.WriteLine($"  Providers queried   : {result.ProvidersQueried}");
        Console.WriteLine($"  Applied             : {result.Applied}");
        Console.WriteLine($"  Staged              : {result.Staged}");
        Console.WriteLine($"  Duplicates skipped  : {result.DuplicatesSkipped}");

        if (result.Errors.Count > 0)
        {
            Console.WriteLine($"  Errors ({result.Errors.Count}):");
            foreach (var error in result.Errors.Take(20))
                Console.WriteLine($"    - {error}");
            if (result.Errors.Count > 20)
                Console.WriteLine($"    ... and {result.Errors.Count - 20} more");
        }

        _log.Information(
            "Corporate-action ingest completed: {Applied} applied, {Staged} staged, {Duplicates} duplicates, {Errors} errors",
            result.Applied,
            result.Staged,
            result.DuplicatesSkipped,
            result.Errors.Count);

        return result.Errors.Count == 0 ? CliResult.Ok() : CliResult.Fail(ErrorCode.ValidationFailed);
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
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Cancellation ends the ingest; it is not a per-ticker outcome.
                throw;
            }
            catch (Exception ex) when (SecurityMasterIngestFailureClassifier.IsAlreadyMastered(ex))
            {
                skipped++;
            }
            catch (Exception ex)
            {
                failed++;
                var ticker = request.Identifiers.FirstOrDefault()?.Value ?? "?";
                errors.Add($"{ticker}: {ex.Message}");
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
            Console.Error.WriteLine("       --security-master-ingest --provider corporate-actions [--symbols AAPL,MSFT] [--minimum-sources N] [--dry-run]");
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

        if (_importService is null)
        {
            Console.Error.WriteLine("Security Master is not configured.");
            Console.Error.WriteLine("Set MERIDIAN_SECURITY_MASTER_CONNECTION_STRING to use this command.");
            _log.Warning("--security-master-ingest invoked but Security Master is not configured");
            return CliResult.Fail(ErrorCode.ConfigurationInvalid);
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

        // Every imported security is recorded against this actor. Prefer an explicitly named
        // operator, fall back to the invoking OS account, and only then to the CLI's own workload
        // identity — an unattended run is legitimately a workload, but it should still say so
        // rather than borrowing a placeholder.
        var importedBy = CliArguments.GetValue(args, "--imported-by") is { Length: > 0 } named
            ? named
            : Environment.UserName is { Length: > 0 } osUser
                ? osUser
                : "meridian-cli";

        var result = await _importService!.ImportAsync(content, extension, importedBy, progress, ct)
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

    private static IReadOnlyList<string> ParseSymbols(string[] args)
    {
        var values = new List<string>();
        var symbolsValue = CliArguments.GetValue(args, "--symbols");
        if (!string.IsNullOrWhiteSpace(symbolsValue))
        {
            values.AddRange(symbolsValue
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        var symbolValue = CliArguments.GetValue(args, "--symbol");
        if (!string.IsNullOrWhiteSpace(symbolValue))
            values.Add(symbolValue.Trim());

        return values
            .Where(static symbol => !string.IsNullOrWhiteSpace(symbol))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static symbol => symbol, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
