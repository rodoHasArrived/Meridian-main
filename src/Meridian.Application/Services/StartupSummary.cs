using System.Text;
using Meridian.Application.Config;
using Meridian.Application.Logging;
using Serilog;

namespace Meridian.Application.Services;

/// <summary>
/// Generates and displays a user-friendly summary of the configuration at startup.
/// Helps users understand what the application will do before it starts.
/// </summary>
public sealed class StartupSummary
{
    private readonly ILogger _log = LoggingSetup.ForContext<StartupSummary>();
    private readonly TextWriter _output;

    public StartupSummary(TextWriter? output = null)
    {
        _output = output ?? Console.Out;
    }

    /// <summary>
    /// Displays a comprehensive startup summary.
    /// </summary>
    public void Display(AppConfig config, string configPath, string[] args)
    {
        var sb = new StringBuilder();

        // Determine mode
        var resolvedMode = CliModeResolver.Resolve(args);
        var port = GetArgValue(args, "--http-port") ?? "8080";
        var mode = resolvedMode == CliModeResolver.RunMode.Desktop
            ? $"Desktop | Port: {port}"
            : "Headless";

        if (args.Any(a => a.Equals("--backfill", StringComparison.OrdinalIgnoreCase)) || config.Backfill?.Enabled == true)
        {
            mode = "Backfill";
        }

        // Health matrix header
        sb.AppendLine();
        sb.AppendLine("╔══════════════════════════════════════════╗");
        sb.AppendLine("║  Meridian v1.6.2            ║");
        sb.AppendLine($"║  Mode: {mode,-33}║");
        sb.AppendLine("╠══════════════════════════════════════════╣");

        // Providers section
        sb.AppendLine("║  Providers:                              ║");
        AppendProviderStatus(sb, "Alpaca", config.DataSource == DataSourceKind.Alpaca, HasAlpacaCredentials(config));
        AppendProviderStatus(sb, "Polygon", config.DataSource == DataSourceKind.Polygon, HasPolygonCredentials(config));
        AppendProviderStatus(sb, "IB", config.DataSource == DataSourceKind.IB, HasIBConfig(config));
        AppendProviderStatus(sb, "NYSE", config.DataSource == DataSourceKind.NYSE, HasNYSECredentials());
        AppendProviderStatus(sb, "Synthetic", config.DataSource == DataSourceKind.Synthetic, config.Synthetic?.Enabled == true);

        // Storage section
        sb.AppendLine("║  Storage:                                ║");
        var dataRootExists = Directory.Exists(config.DataRoot);
        AppendHealthRow(sb, "JSONL sink", dataRootExists);
        AppendHealthRow(sb, "Parquet sink", config.Storage?.EnableParquetSink == true);
        AppendHealthRow(sb, "Compression", config.Compress == true);

        // Symbols
        var symbolCount = config.Symbols?.Length ?? 0;
        sb.AppendLine($"║  Symbols:        {symbolCount,-23}║");

        // Backfill
        var backfillStatus = config.Backfill?.Enabled == true ? "Enabled" : "Disabled";
        sb.AppendLine($"║  Backfill:       {backfillStatus,-23}║");

        sb.AppendLine("╚══════════════════════════════════════════╝");
        sb.AppendLine();

        // Configuration source
        sb.AppendLine($"  Configuration: {configPath}");
        sb.AppendLine();

        // Data Source details
        sb.AppendLine("  Data Source:");
        sb.AppendLine($"    Provider:     {GetProviderDisplayName(config.DataSource)}");

        if (config.DataSource == DataSourceKind.Alpaca && config.Alpaca != null)
        {
            var feed = config.Alpaca.Feed switch
            {
                "iex" => "IEX (free, ~10% of trades)",
                "sip" => "SIP (paid, full market data)",
                "delayed_sip" => "Delayed SIP (free, 15-min delay)",
                _ => config.Alpaca.Feed
            };
            sb.AppendLine($"    Feed:         {feed}");
            sb.AppendLine($"    Environment:  {(config.Alpaca.UseSandbox ? "Sandbox (Paper)" : "Live")}");
        }
        else if (config.DataSource == DataSourceKind.IB && config.IB != null)
        {
            sb.AppendLine($"    Host:         {config.IB.Host}:{config.IB.Port}");
            sb.AppendLine($"    Environment:  {(config.IB.UsePaperTrading ? "Paper Trading" : "Live")}");
        }
        else if (config.DataSource == DataSourceKind.Synthetic && config.Synthetic != null)
        {
            sb.AppendLine($"    Seed:         {config.Synthetic.Seed}");
            sb.AppendLine($"    Universe:     {string.Join(", ", config.Synthetic.UniverseSymbols ?? Array.Empty<string>())}");
            sb.AppendLine($"    History:      {config.Synthetic.DefaultHistoryStart?.ToString() ?? "auto"} → {config.Synthetic.DefaultHistoryEnd?.ToString() ?? "auto"}");
        }
        sb.AppendLine();

        // Symbols details
        var symbols = config.Symbols ?? Array.Empty<SymbolConfig>();
        sb.AppendLine("  Symbols:");
        sb.AppendLine($"    Count:        {symbols.Length}");

        if (symbols.Length > 0)
        {
            var symbolList = symbols.Length <= 5
                ? string.Join(", ", symbols.Select(s => s.Symbol))
                : string.Join(", ", symbols.Take(5).Select(s => s.Symbol)) + $" (+{symbols.Length - 5} more)";
            sb.AppendLine($"    List:         {symbolList}");

            var withDepth = symbols.Count(s => s.SubscribeDepth);
            var withTrades = symbols.Count(s => s.SubscribeTrades);
            sb.AppendLine($"    Trades:       {withTrades} symbols");
            sb.AppendLine($"    Market Depth: {withDepth} symbols");
        }
        sb.AppendLine();

        // Storage details
        sb.AppendLine("  Storage:");
        sb.AppendLine($"    Root:         {Path.GetFullPath(config.DataRoot)}");
        sb.AppendLine($"    Compression:  {(config.Compress ?? false ? "Enabled (gzip)" : "Disabled")}");

        if (config.Storage != null)
        {
            sb.AppendLine($"    Naming:       {config.Storage.NamingConvention}");
            sb.AppendLine($"    Partitioning: {config.Storage.DatePartition}");

            if (config.Storage.RetentionDays.HasValue)
            {
                sb.AppendLine($"    Retention:    {config.Storage.RetentionDays} days");
            }
            if (config.Storage.MaxTotalMegabytes.HasValue)
            {
                sb.AppendLine($"    Max Size:     {config.Storage.MaxTotalMegabytes} MB");
            }
        }
        sb.AppendLine();

        // Backfill details
        if (config.Backfill != null)
        {
            sb.AppendLine("  Backfill:");
            sb.AppendLine($"    Enabled:      {config.Backfill.Enabled}");
            sb.AppendLine($"    Provider:     {config.Backfill.Provider}");
            sb.AppendLine($"    Fallback:     {(config.Backfill.EnableFallback ? "Enabled" : "Disabled")}");

            if (config.Backfill.ProviderPriority?.Length > 0)
            {
                var priority = string.Join(" -> ", config.Backfill.ProviderPriority.Take(4));
                if (config.Backfill.ProviderPriority.Length > 4)
                {
                    priority += " -> ...";
                }
                sb.AppendLine($"    Priority:     {priority}");
            }
            sb.AppendLine();
        }

        // Hot reload
        if (args.Any(a => a.Equals("--watch-config", StringComparison.OrdinalIgnoreCase)))
        {
            sb.AppendLine("  Hot Reload: Enabled");
            sb.AppendLine();
        }

        // Quick tips
        sb.AppendLine("  Tips:");
        sb.AppendLine("    - Press Ctrl+C to stop gracefully");
        sb.AppendLine("    - Check logs in: logs/ directory");
        if (resolvedMode != CliModeResolver.RunMode.Desktop)
        {
            sb.AppendLine("    - Add --mode desktop to start the desktop-local API host");
        }
        sb.AppendLine();

        _output.Write(sb.ToString());
    }

    private static void AppendProviderStatus(StringBuilder sb, string name, bool isActive, bool hasCredentials)
    {
        string status;
        string indicator;

        if (isActive && hasCredentials)
        {
            indicator = "+";
            status = "Active";
        }
        else if (isActive && !hasCredentials)
        {
            indicator = "!";
            status = "No credentials";
        }
        else if (hasCredentials)
        {
            indicator = "-";
            status = "Available";
        }
        else
        {
            return; // Don't show providers that aren't active and have no credentials
        }

        var line = $"    [{indicator}] {name,-12} {status}";
        sb.AppendLine($"║  {line,-38}║");
    }

    private static void AppendHealthRow(StringBuilder sb, string component, bool ready)
    {
        var indicator = ready ? "+" : "-";
        var status = ready ? "Ready" : "Off";
        var line = $"    [{indicator}] {component,-12} {status}";
        sb.AppendLine($"║  {line,-38}║");
    }

    private static bool HasAlpacaCredentials(AppConfig config)
    {
        return !string.IsNullOrEmpty(config.Alpaca?.KeyId) ||
               !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ALPACA__KEYID")) ||
               !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ALPACA_KEY_ID"));
    }

    private static bool HasPolygonCredentials(AppConfig config)
    {
        return !string.IsNullOrEmpty(config.Polygon?.ApiKey) ||
               !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("POLYGON__APIKEY")) ||
               !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("POLYGON_API_KEY"));
    }

    private static bool HasIBConfig(AppConfig config)
    {
        return config.IB != null && !string.IsNullOrEmpty(config.IB.Host);
    }

    private static bool HasNYSECredentials()
    {
        return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NYSE__APIKEY")) ||
               !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NYSE_API_KEY"));
    }

    /// <summary>
    /// Displays a compact one-line status.
    /// </summary>
    public void DisplayCompact(AppConfig config)
    {
        var symbols = config.Symbols?.Length ?? 0;
        var provider = GetProviderDisplayName(config.DataSource);

        _output.WriteLine($"  [{provider}] {symbols} symbols -> {config.DataRoot}");
    }

    /// <summary>
    /// Displays a quick status check.
    /// </summary>
    public QuickCheckResult PerformQuickCheck(AppConfig config)
    {
        var issues = new List<QuickCheckIssue>();
        var suggestions = new List<string>();

        // Check data directory
        if (!Directory.Exists(config.DataRoot))
        {
            issues.Add(new QuickCheckIssue(
                Severity: IssueSeverity.Info,
                Component: "Storage",
                Message: $"Data directory will be created: {config.DataRoot}"
            ));
        }
        else
        {
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(config.DataRoot)) ?? "/");
                var freeGb = drive.AvailableFreeSpace / (1024.0 * 1024 * 1024);

                if (freeGb < 1)
                {
                    issues.Add(new QuickCheckIssue(
                        Severity: IssueSeverity.Warning,
                        Component: "Storage",
                        Message: $"Low disk space: {freeGb:F1} GB available"
                    ));
                    suggestions.Add("Consider enabling compression or setting a storage limit");
                }
            }
            catch (IOException) { /* Ignore disk check errors - drive may be unavailable */ }
        }

        // Check symbols
        if (config.Symbols == null || config.Symbols.Length == 0)
        {
            issues.Add(new QuickCheckIssue(
                Severity: IssueSeverity.Warning,
                Component: "Symbols",
                Message: "No symbols configured, will use default (SPY)"
            ));
        }

        // Check provider credentials
        if (config.DataSource == DataSourceKind.Alpaca)
        {
            var hasKey = !string.IsNullOrEmpty(config.Alpaca?.KeyId) ||
                         !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ALPACA_KEY_ID"));

            if (!hasKey)
            {
                issues.Add(new QuickCheckIssue(
                    Severity: IssueSeverity.Error,
                    Component: "Alpaca",
                    Message: "Alpaca credentials not configured"
                ));
                suggestions.Add("Set ALPACA_KEY_ID and ALPACA_SECRET_KEY environment variables");
            }
        }

        // Check compression recommendation
        if (!(config.Compress ?? false) && (config.Symbols?.Length ?? 0) > 10)
        {
            suggestions.Add("Consider enabling compression for better storage efficiency");
        }

        return new QuickCheckResult(
            Success: !issues.Any(i => i.Severity == IssueSeverity.Error),
            Issues: issues,
            Suggestions: suggestions
        );
    }

    /// <summary>
    /// Displays the quick check results.
    /// </summary>
    public void DisplayQuickCheck(QuickCheckResult result)
    {
        _output.WriteLine();
        _output.WriteLine("  Quick Configuration Check:");
        _output.WriteLine("  " + new string('-', 50));

        if (result.Issues.Count == 0)
        {
            _output.WriteLine("    [OK] All checks passed");
        }
        else
        {
            foreach (var issue in result.Issues)
            {
                var icon = issue.Severity switch
                {
                    IssueSeverity.Error => "[X]",
                    IssueSeverity.Warning => "[!]",
                    IssueSeverity.Info => "[i]",
                    _ => "[-]"
                };
                _output.WriteLine($"    {icon} {issue.Component}: {issue.Message}");
            }
        }

        if (result.Suggestions.Count > 0)
        {
            _output.WriteLine();
            _output.WriteLine("  Suggestions:");
            foreach (var suggestion in result.Suggestions)
            {
                _output.WriteLine($"    - {suggestion}");
            }
        }

        _output.WriteLine();
        _output.WriteLine(result.Success
            ? "  Status: Ready to start"
            : "  Status: Configuration issues detected (see above)");
        _output.WriteLine();
    }

    private static string GetProviderDisplayName(DataSourceKind kind)
    {
        return kind switch
        {
            DataSourceKind.IB => "Interactive Brokers",
            DataSourceKind.Alpaca => "Alpaca Markets",
            DataSourceKind.Polygon => "Polygon.io",
            DataSourceKind.NYSE => "NYSE",
            DataSourceKind.Synthetic => "Synthetic Offline Dataset",
            _ => kind.ToString()
        };
    }

    private static string? GetArgValue(string[] args, string key)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(key, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }
}

/// <summary>
/// Result of a quick configuration check.
/// </summary>
public sealed record QuickCheckResult(
    bool Success,
    IReadOnlyList<QuickCheckIssue> Issues,
    IReadOnlyList<string> Suggestions
);

/// <summary>
/// An issue found during quick check.
/// </summary>
public sealed record QuickCheckIssue(
    IssueSeverity Severity,
    string Component,
    string Message
);

/// <summary>
/// Severity level for quick check issues.
/// </summary>
public enum IssueSeverity : byte
{
    Info,
    Warning,
    Error
}
