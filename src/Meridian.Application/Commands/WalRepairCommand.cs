using Meridian.Application.Composition;
using Meridian.Core.Config;
using Meridian.Platform.Results;
using Meridian.Storage.Archival;
using Serilog;

namespace Meridian.Application.Commands;

/// <summary>
/// Handles the --wal-repair CLI command.
/// Scans all WAL files for corrupted records, rewrites valid records only,
/// and optionally dumps corrupted record sequences for manual inspection.
/// </summary>
internal sealed class WalRepairCommand : ICliCommand
{
    private readonly AppConfig _cfg;
    private readonly ILogger _log;

    public WalRepairCommand(AppConfig cfg, ILogger log)
    {
        _cfg = cfg;
        _log = log;
    }

    public IReadOnlyList<string> Triggers { get; } = ["--wal-repair"];

    public bool CanHandle(string[] args) => CliArguments.MatchesAnyFlag(args, Triggers);

    public async Task<CliResult> ExecuteAsync(string[] args, CancellationToken ct = default)
    {
        var workloadArg = CliArguments.GetValue(args, "--workload");
        PipelineWorkload? onlyWorkload = null;
        if (workloadArg is not null)
        {
            if (!PipelineDurabilityPaths.TryParse(workloadArg, out var parsed))
            {
                Console.WriteLine();
                Console.WriteLine($"  Unknown --workload '{workloadArg}'. Expected one of: " +
                    string.Join(", ", PipelineDurabilityPaths.AllWorkloads.Select(PipelineDurabilityPaths.DirectoryName)));
                Console.WriteLine();
                return CliResult.Fail(ErrorCode.ValidationFailed);
            }

            onlyWorkload = parsed;
        }

        var walDirs = ResolveWalDirectories(_cfg.DataRoot, onlyWorkload);
        if (walDirs.Count == 0)
        {
            Console.WriteLine();
            Console.WriteLine($"  No WAL directories found under: {PipelineDurabilityPaths.LegacyWalDirectory(_cfg.DataRoot)}");
            Console.WriteLine("  Nothing to repair.");
            Console.WriteLine();
            return CliResult.Ok();
        }

        CliResult? failure = null;
        foreach (var walDir in walDirs)
        {
            var result = await RepairDirectoryAsync(walDir, args, ct).ConfigureAwait(false);
            if (!result.Success)
                failure ??= result;
        }

        return failure ?? CliResult.Ok();
    }

    /// <summary>
    /// Every workload's WAL directory that exists, plus the pre-workload shared directory while it
    /// still holds segments a collector has not adopted yet.
    /// </summary>
    internal static IReadOnlyList<string> ResolveWalDirectories(string dataRoot, PipelineWorkload? onlyWorkload)
    {
        if (onlyWorkload is { } workload)
        {
            var dir = PipelineDurabilityPaths.WalDirectory(dataRoot, workload);
            return Directory.Exists(dir) ? [dir] : [];
        }

        var dirs = PipelineDurabilityPaths.AllWorkloads
            .Select(w => PipelineDurabilityPaths.WalDirectory(dataRoot, w))
            .Where(Directory.Exists)
            .ToList();
        var legacy = PipelineDurabilityPaths.LegacyWalDirectory(dataRoot);
        if (Directory.Exists(legacy) && Directory.GetFiles(legacy, "*.wal").Length > 0)
            dirs.Add(legacy);
        return dirs;
    }

    private async Task<CliResult> RepairDirectoryAsync(string walDir, string[] args, CancellationToken ct)
    {
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

        if (dryRun)
        {
            // In dry-run mode, just scan and report without rewriting files
            return await RunDryRepairAsync(walDir, walFiles, outputPath, ct);
        }

        var walOptions = new WalOptions
        {
            SyncMode = WalSyncMode.NoSync,
            CorruptionMode = WalCorruptionMode.Alert
        };

        await using var wal = new WriteAheadLog(walDir, walOptions);
        long corruptedDetected = 0;
        wal.CorruptionDetected += count => corruptedDetected += count;

        _log.Information("Starting WAL repair in {WalDir}", walDir);

        WalRepairResult result;
        try
        {
            // RepairAsync takes the directory's owner lock, so a repair never rewrites segments
            // a running collector or backfill is still writing.
            result = await wal.RepairAsync(ct);
        }
        catch (WalDirectoryInUseException ex)
        {
            _log.Error(ex, "WAL repair refused for {WalDir}: the directory is owned by a running process", walDir);
            Console.WriteLine("  ERROR: this WAL directory is in use by a running Meridian process.");
            Console.WriteLine("  Stop that process before repairing. No files were changed.");
            Console.WriteLine();
            return CliResult.Fail(ErrorCode.FileAccessDenied);
        }

        Console.WriteLine("  Repair Results");
        Console.WriteLine("  " + new string('-', 50));
        Console.WriteLine($"  Total records scanned:   {result.TotalRecords:N0}");
        Console.WriteLine($"  Valid records kept:      {result.ValidRecords:N0}");
        Console.WriteLine($"  Corrupted records found: {result.CorruptedRecords:N0}");
        Console.WriteLine($"  WAL files repaired:      {result.RepairedFiles}");
        Console.WriteLine();

        if (result.CorruptedRecords > 0)
        {
            Console.WriteLine($"  WARNING: {result.CorruptedRecords} corrupted record(s) were discarded.");
            Console.WriteLine("  These records could not be recovered and have been permanently removed.");
            Console.WriteLine();

            if (!string.IsNullOrWhiteSpace(outputPath))
            {
                await WriteRepairReportAsync(outputPath, result, ct);
                Console.WriteLine($"  Repair report written to: {outputPath}");
                Console.WriteLine();
            }
            else
            {
                Console.WriteLine("  Tip: use --output <path> to save a detailed repair report.");
                Console.WriteLine();
            }
        }
        else
        {
            Console.WriteLine("  All WAL records are valid. No corruption found.");
            Console.WriteLine();
        }

        return result.CorruptedRecords > 0
            ? CliResult.Fail(ErrorCode.DataCorruption)
            : CliResult.Ok();
    }

    private static async Task<CliResult> RunDryRepairAsync(
        string walDir,
        string[] walFiles,
        string? outputPath,
        CancellationToken ct)
    {
        int totalRecords = 0;
        int corruptedRecords = 0;
        var corruptedInfo = new List<string>();

        foreach (var walFile in walFiles.OrderBy(f => f))
        {
            ct.ThrowIfCancellationRequested();

            await using var stream = new FileStream(
                walFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);

            var header = await reader.ReadLineAsync(ct);
            if (header == null ||
                (!header.StartsWith("MDCWAL01|", StringComparison.Ordinal) &&
                 !header.StartsWith("MDCWAL02|", StringComparison.Ordinal)))
            {
                Console.WriteLine($"  WARNING: {Path.GetFileName(walFile)} has an invalid header.");
                continue;
            }

            int lineNum = 1;
            while (!reader.EndOfStream && !ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                lineNum++;
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                totalRecords++;
                var parts = line.Split('|', 5);
                if (parts.Length < 5 ||
                    !long.TryParse(parts[0], out _) ||
                    !DateTime.TryParse(parts[1], System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.RoundtripKind, out _))
                {
                    corruptedRecords++;
                    corruptedInfo.Add($"    {Path.GetFileName(walFile)}:{lineNum} — malformed record");
                    continue;
                }

                // We don't re-validate checksums in dry-run; we just count structural parse errors
                // for a quick diagnostic. A full repair is needed to validate checksums.
            }
        }

        Console.WriteLine("  Dry-Run Scan Results");
        Console.WriteLine("  " + new string('-', 50));
        Console.WriteLine($"  Total records scanned:      {totalRecords:N0}");
        Console.WriteLine($"  Structurally corrupt lines: {corruptedRecords:N0}");
        Console.WriteLine();

        if (corruptedRecords > 0)
        {
            Console.WriteLine("  Corrupted locations:");
            foreach (var info in corruptedInfo.Take(20))
                Console.WriteLine(info);
            if (corruptedInfo.Count > 20)
                Console.WriteLine($"    ... and {corruptedInfo.Count - 20} more");
            Console.WriteLine();
            Console.WriteLine("  Run without --dry-run to perform the actual repair.");
        }
        else
        {
            Console.WriteLine("  No structural corruption found in dry-run scan.");
            Console.WriteLine("  Run without --dry-run for full checksum validation.");
        }

        Console.WriteLine();

        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            await AtomicFileWriter.WriteAsync(outputPath, string.Join(Environment.NewLine, corruptedInfo) + Environment.NewLine, ct);
            Console.WriteLine($"  Dry-run report written to: {outputPath}");
            Console.WriteLine();
        }

        return CliResult.Ok();
    }

    private static async Task WriteRepairReportAsync(
        string outputPath,
        WalRepairResult result,
        CancellationToken ct)
    {
        var lines = new List<string>
        {
            $"WAL Repair Report — {DateTime.UtcNow:O}",
            new string('=', 60),
            $"Total records scanned:   {result.TotalRecords}",
            $"Valid records kept:      {result.ValidRecords}",
            $"Corrupted records:       {result.CorruptedRecords}",
            $"WAL files repaired:      {result.RepairedFiles}",
            string.Empty,
            "Corrupted records were permanently discarded during repair.",
            "Recovery from primary storage may be required if these sequences",
            "represent data that was not yet committed to primary storage."
        };

        await AtomicFileWriter.WriteAsync(outputPath, string.Join(Environment.NewLine, lines) + Environment.NewLine, ct);
    }
}
