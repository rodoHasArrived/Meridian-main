namespace Meridian.Application.Commands;

/// <summary>
/// Typed representation of all CLI arguments, parsed once from the raw args array.
/// Eliminates scattered args.Any() calls throughout Program.cs.
/// </summary>
public sealed record CliArguments
{
    // Mode flags
    public bool Help { get; init; }
    public bool SelfTest { get; init; }
    public bool SimulateFeed { get; init; }
    public bool Backfill { get; init; }
    public bool DryRun { get; init; }
    public bool Quickstart { get; init; }
    public bool Offline { get; init; }
    public bool ValidateConfig { get; init; }
    public bool ValidateSchemas { get; init; }
    public bool StrictSchemas { get; init; }
    public bool WatchConfig { get; init; }

    /// <summary>
    /// Alias for <c>--dry-run --offline</c>: validates configuration without
    /// performing any connectivity checks. Equivalent to 3.2 --check-config.
    /// </summary>
    public bool CheckConfig { get; init; }

    // Backfill resume
    /// <summary>
    /// Whether to resume a previously interrupted backfill using persisted checkpoints.
    /// </summary>
    public bool Resume { get; init; }

    // Provider recommendation
    /// <summary>
    /// Whether to print a provider recommendation report and exit.
    /// </summary>
    public bool RecommendProviders { get; init; }

    // Symbol management
    public bool Symbols { get; init; }
    public bool SymbolsMonitored { get; init; }
    public bool SymbolsArchived { get; init; }
    public string? SymbolsAdd { get; init; }
    public string? SymbolsRemove { get; init; }
    public string? SymbolsImport { get; init; }
    public string? SymbolStatus { get; init; }

    // Symbol options
    public bool NoTrades { get; init; }
    public bool NoDepth { get; init; }
    public int DepthLevels { get; init; } = 10;
    public bool Update { get; init; }

    // Backfill options
    public string? BackfillProvider { get; init; }
    public string? BackfillSymbols { get; init; }
    public string? BackfillFrom { get; init; }
    public string? BackfillTo { get; init; }

    // Paths and config
    public string? ConfigPath { get; init; }
    public string? Replay { get; init; }
    public int? HttpPort { get; init; }
    public string? Mode { get; init; }

    /// <summary>
    /// The original raw arguments array, available for commands that need direct access.
    /// </summary>
    public string[] Raw { get; init; } = [];

    /// <summary>
    /// Returns true if any symbol management flag is present.
    /// </summary>
    public bool HasSymbolCommand =>
        Symbols || SymbolsMonitored || SymbolsArchived ||
        SymbolsAdd != null || SymbolsRemove != null || SymbolsImport != null || SymbolStatus != null;

    /// <summary>
    /// Parses the raw args array into a typed CliArguments record.
    /// </summary>
    public static CliArguments Parse(string[] args)
    {
        return new CliArguments
        {
            Raw = args,

            Help = HasFlag(args, "--help") || HasFlag(args, "-h"),
            SelfTest = HasFlag(args, "--selftest"),
            SimulateFeed = HasFlag(args, "--simulate-feed"),
            Backfill = HasFlag(args, "--backfill"),
            DryRun = HasFlag(args, "--dry-run") || HasFlag(args, "--check-config"),
            Quickstart = HasFlag(args, "--quickstart"),
            Offline = HasFlag(args, "--offline") || HasFlag(args, "--check-config"),
            ValidateConfig = HasFlag(args, "--validate-config"),
            ValidateSchemas = HasFlag(args, "--validate-schemas"),
            StrictSchemas = HasFlag(args, "--strict-schemas"),
            WatchConfig = HasFlag(args, "--watch-config"),
            CheckConfig = HasFlag(args, "--check-config"),
            Resume = HasFlag(args, "--resume"),
            RecommendProviders = HasFlag(args, "--recommend-providers"),

            Symbols = HasFlag(args, "--symbols"),
            SymbolsMonitored = HasFlag(args, "--symbols-monitored"),
            SymbolsArchived = HasFlag(args, "--symbols-archived"),
            SymbolsAdd = GetValue(args, "--symbols-add"),
            SymbolsRemove = GetValue(args, "--symbols-remove"),
            SymbolsImport = GetValue(args, "--symbols-import"),
            SymbolStatus = GetValue(args, "--symbol-status"),

            NoTrades = HasFlag(args, "--no-trades"),
            NoDepth = HasFlag(args, "--no-depth"),
            DepthLevels = int.TryParse(GetValue(args, "--depth-levels"), out var levels) ? levels : 10,
            Update = HasFlag(args, "--update"),

            BackfillProvider = GetValue(args, "--backfill-provider"),
            BackfillSymbols = GetValue(args, "--backfill-symbols"),
            BackfillFrom = GetValue(args, "--backfill-from"),
            BackfillTo = GetValue(args, "--backfill-to"),

            ConfigPath = GetValue(args, "--config"),
            Replay = GetValue(args, "--replay"),
            HttpPort = int.TryParse(GetValue(args, "--http-port"), out var port) ? port : null,
            Mode = GetValue(args, "--mode"),
        };
    }

    /// <summary>
    /// Case-insensitive flag check.
    /// </summary>
    internal static bool HasFlag(string[] args, string flag)
        => args.Any(a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Gets the value following a named argument.
    /// Returns null if the flag is missing or has no subsequent value.
    /// </summary>
    internal static string? GetValue(string[] args, string key)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(key, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }

    /// <summary>
    /// Requires a value for the given flag. Writes an error and returns null if missing.
    /// Eliminates repeated null-check + error boilerplate across commands (B1).
    /// </summary>
    internal static string? RequireValue(string[] args, string flag, string example)
    {
        var value = GetValue(args, flag);
        if (!string.IsNullOrWhiteSpace(value))
            return value;

        Console.Error.WriteLine($"Error: {flag} requires a value");
        Console.Error.WriteLine($"Example: {example}");
        return null;
    }

    /// <summary>
    /// Requires a comma-separated list for the given flag.
    /// Returns null and writes an error if the value is missing.
    /// </summary>
    internal static string[]? RequireList(string[] args, string flag, string example)
    {
        var value = RequireValue(args, flag, example);
        return value?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>
    /// Gets an integer value for the given flag, or the default if missing/invalid.
    /// </summary>
    internal static int GetInt(string[] args, string flag, int defaultValue)
    {
        var raw = GetValue(args, flag);
        return int.TryParse(raw, out var parsed) ? parsed : defaultValue;
    }
}
