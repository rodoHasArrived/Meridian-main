using System.Text.Json;
using Meridian.Application.ResultTypes;
using Meridian.Application.Subscriptions.Services;
using Serilog;

namespace Meridian.Application.Commands;

/// <summary>
/// Handles all symbol management CLI commands:
/// --symbols, --symbols-monitored, --symbols-archived, --symbols-add, --symbols-remove,
/// --symbol-status, --symbols-import, --symbols-export
/// </summary>
internal sealed class SymbolCommands : ICliCommand
{
    private readonly SymbolManagementService _symbolService;
    private readonly ILogger _log;

    public SymbolCommands(SymbolManagementService symbolService, ILogger log)
    {
        _symbolService = symbolService;
        _log = log;
    }

    public bool CanHandle(string[] args)
    {
        return args.Any(a =>
            a.Equals("--symbols", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("--symbols-monitored", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("--symbols-archived", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("--symbols-add", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("--symbols-remove", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("--symbol-status", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("--symbols-import", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("--symbols-export", StringComparison.OrdinalIgnoreCase));
    }

    public async Task<CliResult> ExecuteAsync(string[] args, CancellationToken ct = default)
    {
        if (CliArguments.HasFlag(args, "--symbols"))
        {
            await _symbolService.DisplayAllSymbolsAsync(ct);
            return CliResult.Ok();
        }

        if (CliArguments.HasFlag(args, "--symbols-monitored"))
        {
            var result = _symbolService.GetMonitoredSymbols();
            _symbolService.DisplayMonitoredSymbols(result);
            return CliResult.Ok();
        }

        if (CliArguments.HasFlag(args, "--symbols-archived"))
        {
            var result = await _symbolService.GetArchivedSymbolsAsync(ct: ct);
            _symbolService.DisplayArchivedSymbols(result);
            return CliResult.Ok();
        }

        if (CliArguments.HasFlag(args, "--symbols-add"))
            return await RunAddAsync(args, ct);

        if (CliArguments.HasFlag(args, "--symbols-import"))
            return await RunImportAsync(args, ct);

        if (CliArguments.HasFlag(args, "--symbols-remove"))
            return await RunRemoveAsync(args, ct);

        if (CliArguments.HasFlag(args, "--symbol-status"))
            return await RunStatusAsync(args, ct);

        if (CliArguments.HasFlag(args, "--symbols-export"))
            return RunExport(args);

        return CliResult.Fail(ErrorCode.Unknown);
    }

    private async Task<CliResult> RunAddAsync(string[] args, CancellationToken ct)
    {
        var symbolsToAdd = CliArguments.RequireList(args, "--symbols-add", "--symbols-add AAPL,MSFT,GOOGL");
        if (symbolsToAdd is null)
            return CliResult.Fail(ErrorCode.RequiredFieldMissing);
        var options = new SymbolAddOptions(
            SubscribeTrades: !CliArguments.HasFlag(args, "--no-trades"),
            SubscribeDepth: !CliArguments.HasFlag(args, "--no-depth"),
            DepthLevels: int.TryParse(CliArguments.GetValue(args, "--depth-levels"), out var levels) ? levels : 10,
            UpdateExisting: CliArguments.HasFlag(args, "--update")
        );

        var result = await _symbolService.AddSymbolsAsync(symbolsToAdd, options, ct);
        Console.WriteLine();
        Console.WriteLine(result.Success ? "Symbol Addition Result" : "Symbol Addition Failed");
        Console.WriteLine(new string('=', 50));
        Console.WriteLine($"  {result.Message}");
        if (result.AffectedSymbols.Length > 0)
        {
            Console.WriteLine($"  Symbols: {string.Join(", ", result.AffectedSymbols)}");
        }
        Console.WriteLine();

        return CliResult.FromBool(result.Success, ErrorCode.ValidationFailed);
    }

    private async Task<CliResult> RunImportAsync(string[] args, CancellationToken ct)
    {
        var filePath = CliArguments.RequireValue(args, "--symbols-import", "--symbols-import symbols.csv");
        if (filePath is null)
            return CliResult.Fail(ErrorCode.RequiredFieldMissing);

        if (!File.Exists(filePath))
        {
            Console.Error.WriteLine($"Error: File not found: {filePath}");
            return CliResult.Fail(ErrorCode.FileNotFound);
        }

        var options = new SymbolAddOptions(
            SubscribeTrades: !CliArguments.HasFlag(args, "--no-trades"),
            SubscribeDepth: !CliArguments.HasFlag(args, "--no-depth"),
            DepthLevels: int.TryParse(CliArguments.GetValue(args, "--depth-levels"), out var levels) ? levels : 10,
            UpdateExisting: CliArguments.HasFlag(args, "--update")
        );

        // Read the file and parse - supports JSON array, CSV, TXT, and one-symbol-per-line formats
        var content = File.ReadAllText(filePath);
        var symbols = ParseSymbolFile(content, filePath);

        if (symbols.Length == 0)
        {
            Console.Error.WriteLine("Error: No valid symbols found in the file.");
            Console.Error.WriteLine("Expected formats:");
            Console.Error.WriteLine("  - One symbol per line: AAPL\\nMSFT\\nGOOGL");
            Console.Error.WriteLine("  - Comma-separated: AAPL,MSFT,GOOGL");
            Console.Error.WriteLine("  - CSV with header: Symbol,Name\\nAAPL,Apple Inc.");
            return CliResult.Fail(ErrorCode.ValidationFailed);
        }

        Console.WriteLine();
        Console.WriteLine($"Importing {symbols.Length} symbols from {filePath}...");
        Console.WriteLine(new string('=', 50));

        var result = await _symbolService.AddSymbolsAsync(symbols, options, ct);

        Console.WriteLine($"  {result.Message}");
        if (result.AffectedSymbols.Length > 0)
        {
            Console.WriteLine($"  Added: {string.Join(", ", result.AffectedSymbols)}");
        }
        Console.WriteLine();

        _log.Information("Bulk symbol import from {FilePath}: {Count} symbols, success={Success}",
            filePath, symbols.Length, result.Success);

        return CliResult.FromBool(result.Success, ErrorCode.ValidationFailed);
    }

    private async Task<CliResult> RunRemoveAsync(string[] args, CancellationToken ct)
    {
        var symbolsToRemove = CliArguments.RequireList(args, "--symbols-remove", "--symbols-remove AAPL,MSFT");
        if (symbolsToRemove is null)
            return CliResult.Fail(ErrorCode.RequiredFieldMissing);
        var result = await _symbolService.RemoveSymbolsAsync(symbolsToRemove, ct);

        Console.WriteLine();
        Console.WriteLine(result.Success ? "Symbol Removal Result" : "Symbol Removal Failed");
        Console.WriteLine(new string('=', 50));
        Console.WriteLine($"  {result.Message}");
        if (result.AffectedSymbols.Length > 0)
        {
            Console.WriteLine($"  Removed: {string.Join(", ", result.AffectedSymbols)}");
        }
        Console.WriteLine();

        return CliResult.FromBool(result.Success, ErrorCode.ValidationFailed);
    }

    private async Task<CliResult> RunStatusAsync(string[] args, CancellationToken ct)
    {
        var symbolArg = CliArguments.RequireValue(args, "--symbol-status", "--symbol-status AAPL");
        if (symbolArg is null)
            return CliResult.Fail(ErrorCode.RequiredFieldMissing);

        var status = await _symbolService.GetSymbolStatusAsync(symbolArg, ct);

        Console.WriteLine();
        Console.WriteLine($"Symbol Status: {status.Symbol}");
        Console.WriteLine(new string('=', 50));
        Console.WriteLine($"  Monitored: {(status.IsMonitored ? "Yes" : "No")}");
        Console.WriteLine($"  Has Archived Data: {(status.HasArchivedData ? "Yes" : "No")}");

        if (status.MonitoredConfig != null)
        {
            Console.WriteLine();
            Console.WriteLine("  Monitoring Configuration:");
            Console.WriteLine($"    Subscribe Trades: {status.MonitoredConfig.SubscribeTrades}");
            Console.WriteLine($"    Subscribe Depth: {status.MonitoredConfig.SubscribeDepth}");
            Console.WriteLine($"    Depth Levels: {status.MonitoredConfig.DepthLevels}");
            Console.WriteLine($"    Security Type: {status.MonitoredConfig.SecurityType}");
            Console.WriteLine($"    Exchange: {status.MonitoredConfig.Exchange}");
        }

        if (status.ArchivedInfo != null)
        {
            Console.WriteLine();
            Console.WriteLine("  Archived Data:");
            Console.WriteLine($"    Files: {status.ArchivedInfo.FileCount}");
            Console.WriteLine($"    Size: {FormatBytes(status.ArchivedInfo.TotalSizeBytes)}");
            if (status.ArchivedInfo.OldestData.HasValue && status.ArchivedInfo.NewestData.HasValue)
            {
                Console.WriteLine($"    Date Range: {status.ArchivedInfo.OldestData:yyyy-MM-dd} to {status.ArchivedInfo.NewestData:yyyy-MM-dd}");
            }
            if (status.ArchivedInfo.DataTypes.Length > 0)
            {
                Console.WriteLine($"    Data Types: {string.Join(", ", status.ArchivedInfo.DataTypes)}");
            }
        }

        Console.WriteLine();
        return CliResult.Ok();
    }

    private CliResult RunExport(string[] args)
    {
        var filePath = CliArguments.RequireValue(args, "--symbols-export",
            "--symbols-export symbols.txt");
        if (filePath is null)
            return CliResult.Fail(ErrorCode.RequiredFieldMissing);

        var symbols = _symbolService.GetMonitoredSymbols();
        if (symbols.Symbols.Length == 0)
        {
            Console.Error.WriteLine("Error: No symbols configured for monitoring");
            return CliResult.Fail(ErrorCode.NoDataAvailable);
        }

        var symbolNames = symbols.Symbols.Select(s => s.Symbol).ToArray();
        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        string content;
        if (extension == ".json")
        {
            content = JsonSerializer.Serialize(symbolNames, new JsonSerializerOptions { WriteIndented = true });
        }
        else if (extension == ".csv")
        {
            content = string.Join(",", symbolNames);
        }
        else
        {
            // Default: one symbol per line
            content = string.Join(Environment.NewLine, symbolNames);
        }

        File.WriteAllText(filePath, content);

        Console.WriteLine();
        Console.WriteLine($"  Exported {symbolNames.Length} symbol(s) to {filePath}");
        Console.WriteLine($"  Symbols: {string.Join(", ", symbolNames.Take(20))}");
        if (symbolNames.Length > 20)
        {
            Console.WriteLine($"  ... and {symbolNames.Length - 20} more");
        }
        Console.WriteLine();

        return CliResult.Ok();
    }

    /// <summary>
    /// Parses symbols from a file, auto-detecting format (one-per-line, CSV, or JSON array).
    /// </summary>
    internal static string[] ParseSymbolFile(string content, string filePath)
    {
        content = content.Trim();
        if (string.IsNullOrEmpty(content))
            return Array.Empty<string>();

        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        // JSON array format
        if (content.StartsWith('[') || extension == ".json")
        {
            try
            {
                var symbols = JsonSerializer.Deserialize<string[]>(content);
                if (symbols != null)
                {
                    return symbols
                        .Select(s => s.Trim().ToUpperInvariant())
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                }
            }
            catch (JsonException)
            {
                // Fall through to line-based parsing
            }
        }

        // CSV format (single line with commas) or one-per-line
        var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        // If it's a single line with commas, treat as CSV
        if (lines.Length == 1 && lines[0].Contains(','))
        {
            return lines[0].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => s.ToUpperInvariant())
                .Where(s => !string.IsNullOrWhiteSpace(s) && !s.StartsWith('#'))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        // Multi-line: one symbol per line (or CSV with header)
        return lines
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith('#') && !l.StartsWith("//"))
            .Select(l => l.Split(',', StringSplitOptions.TrimEntries)[0]) // Take first column (handles CSV with headers)
            .Select(s => s.ToUpperInvariant())
            .Where(s => !string.IsNullOrWhiteSpace(s) && s != "SYMBOL" && s != "TICKER") // Skip CSV headers
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static string FormatBytes(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        var order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.##} {sizes[order]}";
    }
}
