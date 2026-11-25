using Meridian.Platform.Results;
using Meridian.Storage.Archival;
using Serilog;

namespace Meridian.Application.Commands;

/// <summary>
/// Handles the --wal-repair CLI command.
/// Scans all WAL files for corrupted records and rewrites affected segments with only their
/// valid records, after copying each original into the WAL's quarantine folder.
/// </summary>
/// <remarks>
/// A repair needs the WAL directory to itself: it refuses to run while a Meridian process is
/// writing there. <c>--dry-run</c> runs the same checksum-validating scan without modifying
/// anything and may be used beside a running process.
/// </remarks>
internal sealed class WalRepairCommand : ICliCommand
{
    private const int MaxLocationsShown = 20;

    private readonly string _walDirectory;
    private readonly ILogger _log;

    /// <param name="dataRoot">The resolved data root the pipeline writes under.</param>
    /// <param name="log">Logger.</param>
    public WalRepairCommand(string dataRoot, ILogger log)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        _walDirectory = Path.Combine(dataRoot, "_wal");
        _log = log;
    }

    public IReadOnlyList<string> Triggers { get; } = ["--wal-repair"];

    public bool CanHandle(string[] args) => CliArguments.MatchesAnyFlag(args, Triggers);

    public async Task<CliResult> ExecuteAsync(string[] args, CancellationToken ct = default)
    {
        var walDir = _walDirectory;

        if (!Directory.Exists(walDir))
        {
            Console.WriteLine();
            Console.WriteLine($"  WAL directory not found: {walDir}");
            Console.WriteLine("  Nothing to repair.");
            Console.WriteLine();
            return CliResult.Ok();
        }

        var outputPath = CliArguments.GetValue(args, "--output");
        var dryRun = CliArguments.HasFlag(args, "--dry-run");

        Console.WriteLine();
        Console.WriteLine("  WAL Repair");
        Console.WriteLine("  " + new string('=', 50));
        Console.WriteLine($"  WAL directory: {walDir}");

        if (dryRun)
            Console.WriteLine("  Mode: DRY RUN (no files will be modified)");

        Console.WriteLine();

        var walFiles = Directory.GetFiles(walDir, "*.wal");
        if (walFiles.Length == 0)
        {
            Console.WriteLine("  No WAL files found.");
            Console.WriteLine();
            return CliResult.Ok();
        }

        Console.WriteLine($"  Found {walFiles.Length} WAL file(s).");
        Console.WriteLine();

        var walOptions = new WalOptions
        {
            SyncMode = WalSyncMode.NoSync,
            CorruptionMode = WalCorruptionMode.Alert
        };

        // Deliberately never initialized: initializing would start a new segment. RepairAsync
        // takes the directory's single-writer lock itself for a real repair.
        await using var wal = new WriteAheadLog(walDir, walOptions);

        _log.Information("Starting WAL repair in {WalDir} (dry run: {DryRun})", walDir, dryRun);

        WalRepairResult result;
        try
        {
            result = await wal.RepairAsync(dryRun, ct);
        }
        catch (WalDirectoryInUseException)
        {
            Console.WriteLine("  ERROR: the WAL directory is in use by a running Meridian process.");
            Console.WriteLine("  Stop that process before repairing, or use --dry-run to scan without changes.");
            Console.WriteLine("  No files were modified.");
            Console.WriteLine();
            return CliResult.Fail(ErrorCode.StorageError);
        }

        Console.WriteLine(dryRun ? "  Dry-Run Scan Results" : "  Repair Results");
        Console.WriteLine("  " + new string('-', 50));
        Console.WriteLine($"  Total records scanned:   {result.TotalRecords:N0}");
        Console.WriteLine($"  Valid records:           {result.ValidRecords:N0}");
        Console.WriteLine($"  Corrupted records found: {result.CorruptedRecords:N0}");
        Console.WriteLine($"  Incomplete final writes: {result.IncompleteTailRecords:N0}");
        if (!dryRun)
            Console.WriteLine($"  WAL files repaired:      {result.RepairedFiles}");
        Console.WriteLine();

        foreach (var finding in result.Findings.Where(f => f.Kind == WalRepairFindingKind.InvalidHeader))
            Console.WriteLine($"  WARNING: {finding.File} has an invalid or unsupported header and was not scanned.");

        if (result.IncompleteTailRecords > 0)
        {
            Console.WriteLine("  Incomplete final writes are interrupted, unacknowledged appends.");
            Console.WriteLine("  Recovery ignores them; they are not corruption and were left in place.");
            Console.WriteLine();
        }

        if (result.CorruptedRecords > 0)
        {
            Console.WriteLine("  Corrupted locations:");
            var corrupt = result.Findings.Where(f => f.Kind == WalRepairFindingKind.CorruptRecord).ToList();
            foreach (var finding in corrupt.Take(MaxLocationsShown))
                Console.WriteLine("    " + Describe(finding));
            if (result.CorruptedRecords > MaxLocationsShown)
                Console.WriteLine($"    ... and {result.CorruptedRecords - MaxLocationsShown} more");
            Console.WriteLine();

            if (dryRun)
            {
                Console.WriteLine("  Run without --dry-run (with Meridian stopped) to perform the repair.");
            }
            else if (result.RepairedFiles > 0)
            {
                Console.WriteLine($"  WARNING: {result.CorruptedRecords} corrupted record(s) were removed from the WAL.");
                Console.WriteLine("  The original segment(s) were kept for inspection:");
                foreach (var path in result.QuarantinedFiles)
                    Console.WriteLine($"    {path}");
            }
            else
            {
                Console.WriteLine("  The affected segment was the most recently written one and was not rewritten.");
            }

            Console.WriteLine();
        }
        else
        {
            Console.WriteLine("  All complete WAL records are valid. No corruption found.");
            Console.WriteLine();
        }

        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            await WriteRepairReportAsync(outputPath, result, dryRun, ct);
            Console.WriteLine($"  {(dryRun ? "Dry-run" : "Repair")} report written to: {outputPath}");
            Console.WriteLine();
        }
        else if (result.CorruptedRecords > 0)
        {
            Console.WriteLine("  Tip: use --output <path> to save a detailed report.");
            Console.WriteLine();
        }

        // A dry run is a diagnostic and reports through its output; only a real repair that
        // found damaged records signals corruption through the exit code.
        return !dryRun && result.CorruptedRecords > 0
            ? CliResult.Fail(ErrorCode.DataCorruption)
            : CliResult.Ok();
    }

    private static string Describe(WalRepairFinding finding)
    {
        var what = finding.Kind switch
        {
            WalRepairFindingKind.CorruptRecord => "corrupt record",
            WalRepairFindingKind.IncompleteTail => "incomplete final write",
            _ => "invalid header"
        };
        return finding.Sequence is { } sequence
            ? $"{finding.File}:{finding.LineNumber} — {what} (sequence {sequence})"
            : $"{finding.File}:{finding.LineNumber} — {what}";
    }

    private static async Task WriteRepairReportAsync(
        string outputPath,
        WalRepairResult result,
        bool dryRun,
        CancellationToken ct)
    {
        var lines = new List<string>
        {
            $"WAL {(dryRun ? "Dry-Run Scan" : "Repair")} Report — {DateTime.UtcNow:O}",
            new string('=', 60),
            $"Total records scanned:   {result.TotalRecords}",
            $"Valid records:           {result.ValidRecords}",
            $"Corrupted records:       {result.CorruptedRecords}",
            $"Incomplete final writes: {result.IncompleteTailRecords}",
            $"WAL files repaired:      {result.RepairedFiles}",
            string.Empty,
            "Findings:"
        };
        lines.AddRange(result.Findings.Select(f => "  " + Describe(f)));

        if (result.QuarantinedFiles.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Original segments kept for inspection:");
            lines.AddRange(result.QuarantinedFiles.Select(path => "  " + path));
            lines.Add(string.Empty);
            lines.Add("Corrupted records were removed from the WAL. Recovery from primary storage");
            lines.Add("may be required if those sequences were not yet committed to primary storage.");
        }

        await AtomicFileWriter.WriteAsync(outputPath, string.Join(Environment.NewLine, lines) + Environment.NewLine, ct);
    }
}
