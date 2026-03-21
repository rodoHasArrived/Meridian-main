using Meridian.Application.Config;
using Meridian.Application.ResultTypes;
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

    public bool CanHandle(string[] args)
        => CliArguments.HasFlag(args, "--wal-repair");

    public async Task<CliResult> ExecuteAsync(string[] args, CancellationToken ct = default)
    {
        var walDir = Path.Combine(_cfg.DataRoot, "_wal");

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

        var result = await wal.RepairAsync(ct);

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
            if (header == null || !header.StartsWith("MDCWAL01", StringComparison.Ordinal))
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
