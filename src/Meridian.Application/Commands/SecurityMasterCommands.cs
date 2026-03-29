using Meridian.Application.ResultTypes;
using Meridian.Application.SecurityMaster;
using Serilog;

namespace Meridian.Application.Commands;

/// <summary>
/// Handles --security-master-ingest CLI command for bulk-importing securities from CSV or JSON.
/// Usage:
///   --security-master-ingest ./securities.csv
///   --security-master-ingest ./securities.json
/// Requires MERIDIAN_SECURITY_MASTER_CONNECTION_STRING to be configured.
/// </summary>
internal sealed class SecurityMasterCommands : ICliCommand
{
    // NOTE: _importService is null when the Security Master database is not configured at CLI
    // startup (e.g. the env var is absent). The full DI host wires the real service.
    private readonly ISecurityMasterImportService? _importService;
    private readonly ILogger _log;

    public SecurityMasterCommands(ISecurityMasterImportService? importService, ILogger log)
    {
        _importService = importService;
        _log = log;
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

        var filePath = CliArguments.GetValue(args, "--security-master-ingest");
        if (string.IsNullOrWhiteSpace(filePath))
        {
            Console.Error.WriteLine("Usage: --security-master-ingest <file.csv|file.json>");
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
            if (p.Processed % 10 == 0 || p.Processed == p.Total)
                Console.WriteLine($"  Progress: {p.Processed}/{p.Total} ({p.Imported} imported, {p.Failed} failed)");
        });

        var result = await _importService.ImportAsync(content, extension, progress, ct)
            .ConfigureAwait(false);

        Console.WriteLine();
        Console.WriteLine("Import complete:");
        Console.WriteLine($"  Imported : {result.Imported}");
        Console.WriteLine($"  Skipped  : {result.Skipped}");
        Console.WriteLine($"  Failed   : {result.Failed}");

        if (result.Errors.Count > 0)
        {
            Console.WriteLine($"  Errors ({result.Errors.Count}):");
            foreach (var error in result.Errors.Take(20))
                Console.WriteLine($"    - {error}");
            if (result.Errors.Count > 20)
                Console.WriteLine($"    ... and {result.Errors.Count - 20} more");
        }

        _log.Information(
            "Security Master ingest completed: {Imported} imported, {Skipped} skipped, {Failed} failed",
            result.Imported, result.Skipped, result.Failed);

        return result.Failed == 0 ? CliResult.Ok() : CliResult.Fail(ErrorCode.ValidationFailed);
    }
}
