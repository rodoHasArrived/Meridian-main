namespace Meridian.Application.Commands;

/// <summary>
/// Handles --help / -h CLI flags.
/// Supports contextual help via --help &lt;topic&gt; (D2 improvement).
/// Available topics: backfill, symbols, config, package, diagnostics, providers.
/// </summary>
internal sealed class HelpCommand : ICliCommand
{
    private static readonly Dictionary<string, Action> s_topics = new(StringComparer.OrdinalIgnoreCase)
    {
        ["backfill"] = ShowBackfillHelp,
        ["symbols"] = ShowSymbolsHelp,
        ["config"] = ShowConfigHelp,
        ["package"] = ShowPackageHelp,
        ["diagnostics"] = ShowDiagnosticsHelp,
        ["providers"] = ShowProvidersHelp,
    };

    public bool CanHandle(string[] args)
    {
        return args.Any(a =>
            a.Equals("--help", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("-h", StringComparison.OrdinalIgnoreCase));
    }

    public Task<CliResult> ExecuteAsync(string[] args, CancellationToken ct = default)
    {
        // Check for --help <topic>
        var topic = CliArguments.GetValue(args, "--help")
                    ?? CliArguments.GetValue(args, "-h");

        if (topic != null && s_topics.TryGetValue(topic, out var showTopic))
        {
            showTopic();
        }
        else if (topic != null && !topic.StartsWith('-'))
        {
            Console.WriteLine($"Unknown help topic: '{topic}'");
            Console.WriteLine();
            ShowTopicList();
        }
        else
        {
            ShowHelp();
        }

        return Task.FromResult(CliResult.Ok());
    }

    private static void ShowTopicList()
    {
        Console.WriteLine("Available help topics:");
        Console.WriteLine("  --help backfill       Historical data backfill options and examples");
        Console.WriteLine("  --help symbols        Symbol management commands");
        Console.WriteLine("  --help config         Configuration and environment variables");
        Console.WriteLine("  --help package        Data packaging and import/export");
        Console.WriteLine("  --help diagnostics    Diagnostics and troubleshooting");
        Console.WriteLine("  --help providers      Data provider information");
        Console.WriteLine();
        Console.WriteLine("Run --help without a topic for the full reference.");
    }

    private static void ShowBackfillHelp()
    {
        Console.WriteLine(@"
BACKFILL - Historical Data Collection
══════════════════════════════════════

Run historical data backfill to fill gaps in your data or bootstrap a new dataset.

OPTIONS:
    --backfill                      Enable backfill mode
    --backfill-provider <name>      Provider to use (default: stooq)
                                    Available: alpaca, polygon, tiingo, yahoo,
                                    stooq, finnhub, alphavantage, nasdaq, ib
    --backfill-symbols <list>       Comma-separated symbols (e.g., AAPL,MSFT)
    --backfill-from <date>          Start date (YYYY-MM-DD)
    --backfill-to <date>            End date (YYYY-MM-DD)
    --resume                        Resume an interrupted backfill from the last
                                    saved checkpoint (skips already-completed symbols)

EXAMPLES:
    # Basic backfill using free provider
    Meridian --backfill --backfill-symbols AAPL,MSFT \
        --backfill-from 2024-01-01 --backfill-to 2024-12-31

    # Use a specific provider
    Meridian --backfill --backfill-provider alpaca \
        --backfill-symbols SPY --backfill-from 2024-01-01

    # Backfill multiple symbols
    Meridian --backfill --backfill-symbols AAPL,MSFT,GOOGL,AMZN,META

PROVIDERS (by rate limit tolerance):
    stooq           Free, no API key, low rate limits
    yahoo           Free, unofficial, moderate limits
    tiingo          Free tier, 500/hour
    finnhub         Free tier, 60/min
    alphavantage    Free tier, 5/min
    alpaca          Free with account, 200/min
    polygon         Paid tiers, varies
    ib              Free with IB account, pacing rules apply

Rate limits are handled automatically with exponential backoff and Retry-After
header parsing. The composite provider will fall back to alternate providers
when rate limits are hit.
");
    }

    private static void ShowSymbolsHelp()
    {
        Console.WriteLine(@"
SYMBOL MANAGEMENT
═════════════════

Manage which symbols are monitored for real-time data collection.

COMMANDS:
    --symbols               Show all symbols (monitored + archived)
    --symbols-monitored     List symbols currently configured for monitoring
    --symbols-archived      List symbols with archived data files
    --symbols-add <list>    Add symbols to configuration (comma-separated)
    --symbols-remove <list> Remove symbols from configuration
    --symbol-status <sym>   Show detailed status for a specific symbol

OPTIONS (use with --symbols-add):
    --no-trades             Don't subscribe to trade data
    --no-depth              Don't subscribe to depth/L2 data
    --depth-levels <n>      Number of depth levels (default: 10)
    --update                Update existing symbols instead of skipping

EXAMPLES:
    # Add symbols with default options (trades + depth)
    Meridian --symbols-add AAPL,MSFT,GOOGL

    # Add symbols for trades only (no depth data)
    Meridian --symbols-add SPY,QQQ --no-depth

    # Add with custom depth levels
    Meridian --symbols-add ES,NQ --depth-levels 20

    # Update existing symbol configuration
    Meridian --symbols-add AAPL --no-depth --update

    # Remove symbols
    Meridian --symbols-remove TSLA,NFLX

    # Check detailed status
    Meridian --symbol-status AAPL
");
    }

    private static void ShowConfigHelp()
    {
        Console.WriteLine(@"
CONFIGURATION
═════════════

Configuration file loading and environment variable mapping.

OPTIONS:
    --config <path>         Path to configuration file (default: appsettings.json)
    --watch-config          Enable hot-reload of configuration changes
    --validate-config       Validate configuration without starting
    --show-config           Display current configuration summary
    --quick-check           Fast configuration health check

ENVIRONMENT VARIABLES:
    MDC_CONFIG_PATH         Alternative to --config for specifying config path
    MDC_ENVIRONMENT         Environment name (loads appsettings.{Env}.json overlay)
    DOTNET_ENVIRONMENT      Standard .NET environment fallback

    ALPACA__KEYID           Alpaca API key ID
    ALPACA__SECRETKEY       Alpaca API secret key
    NYSE__APIKEY            NYSE API key
    POLYGON__APIKEY         Polygon API key
    TIINGO__TOKEN           Tiingo API token
    FINNHUB__TOKEN          Finnhub API token
    ALPHAVANTAGE__APIKEY    Alpha Vantage API key

CONFIG FILE PRIORITY:
    1. --config argument (highest priority)
    2. MDC_CONFIG_PATH environment variable
    3. appsettings.json (default)

FIRST-TIME SETUP:
    --quickstart            Auto-detect, validate, and configure (fastest)
    --wizard                Interactive configuration wizard (step-by-step)
    --auto-config           Auto-configure from environment variables
    --detect-providers      Show available providers and their status
    --generate-config       Generate a config template
    --generate-config-schema Generate JSON Schema for appsettings.json
    --template <name>       Template: minimal, full, alpaca, stocksharp,
                            backfill, production, docker

EXAMPLES:
    # Fastest setup (auto-detect everything)
    Meridian --quickstart

    # Interactive first-time setup
    Meridian --wizard

    # Validate before starting
    Meridian --validate-config --config ./my-config.json

    # Generate a template
    Meridian --generate-config --template alpaca

    # Generate JSON Schema for IDE validation
    Meridian --generate-config-schema --output config/appsettings.schema.json
");
    }

    private static void ShowPackageHelp()
    {
        Console.WriteLine(@"
DATA PACKAGING
══════════════

Create portable data packages for sharing, backup, or archival.

CREATE OPTIONS:
    --package                       Create a portable data package
    --package-name <name>           Package name (default: market-data-YYYYMMDD)
    --package-description <text>    Package description
    --package-output <path>         Output directory (default: packages)
    --package-symbols <list>        Comma-separated symbols to include
    --package-events <list>         Event types (Trade,BboQuote,L2Snapshot)
    --package-from <date>           Start date (YYYY-MM-DD)
    --package-to <date>             End date (YYYY-MM-DD)
    --package-format <fmt>          Format: zip, tar.gz (default: zip)
    --package-compression <level>   Compression: none, fast, balanced, max
    --no-quality-report             Exclude quality report from package
    --no-data-dictionary            Exclude data dictionary
    --no-loader-scripts             Exclude loader scripts
    --skip-checksums                Skip checksum verification

IMPORT OPTIONS:
    --import-package <path>         Import a package into storage
    --import-destination <path>     Destination directory
    --skip-validation               Skip checksum validation during import
    --merge                         Merge with existing data

OTHER:
    --list-package <path>           List package contents
    --validate-package <path>       Validate package integrity

EXAMPLES:
    # Create a package for specific symbols and date range
    Meridian --package --package-name q1-2024 \
        --package-symbols AAPL,MSFT \
        --package-from 2024-01-01 --package-to 2024-03-31

    # Create with maximum compression for archival
    Meridian --package --package-compression max

    # Import a package
    Meridian --import-package ./packages/q1-2024.zip --merge
");
    }

    private static void ShowDiagnosticsHelp()
    {
        Console.WriteLine(@"
DIAGNOSTICS & TROUBLESHOOTING
═════════════════════════════

Tools for diagnosing issues and verifying system health.

COMMANDS:
    --quick-check           Fast configuration health check
    --test-connectivity     Test connectivity to all configured providers
    --show-config           Display current configuration summary
    --error-codes           Show error code reference guide
    --check-schemas         Check stored data schema compatibility
    --wal-repair            Scan WAL files for corruption and rewrite only valid records
    --simulate-feed         Emit a synthetic event for smoke testing
    --selftest              Run comprehensive system self-tests
    --dry-run               Full validation without starting collection
    --check-config          Config-only validation (alias for --dry-run --offline)
    --dry-run --offline     Validation without network connectivity checks
    --validate-credentials  Validate all configured API credentials
    --recommend-providers   Scored provider recommendation report

SCHEMA VALIDATION:
    --validate-schemas      Run schema check during startup
    --strict-schemas        Exit if incompatibilities found
    --max-files <n>         Max files to check (default: 100)
    --fail-fast             Stop on first incompatibility

EXAMPLES:
    # Quick health check (recommended first step)
    Meridian --quick-check

    # Full dry run
    Meridian --dry-run

    # Test all provider connections
    Meridian --test-connectivity

    # Verify stored data schemas
    Meridian --check-schemas --max-files 500
");
    }

    private static void ShowProvidersHelp()
    {
        Console.WriteLine(@"
DATA PROVIDERS
══════════════

Streaming (real-time) and historical data providers.

STREAMING PROVIDERS:
    alpaca          WebSocket streaming, trades + quotes
    polygon         WebSocket streaming, trades + quotes + depth
    ib              Interactive Brokers TWS/Gateway, full L2
    stocksharp      90+ data sources via StockSharp connectors
    nyse            NYSE hybrid streaming + historical

HISTORICAL (BACKFILL) PROVIDERS:
    stooq           Free daily bars, no API key needed
    yahoo           Free daily bars (unofficial API)
    tiingo          Free tier: 500 req/hour
    finnhub         Free tier: 60 req/min
    alphavantage    Free tier: 5 req/min
    alpaca          200 req/min (account required)
    polygon         Varies by plan
    nasdaq          Nasdaq Data Link
    ib              IB historical data (pacing rules)

SYMBOL SEARCH PROVIDERS:
    alpaca          US equities + crypto
    finnhub         US + international exchanges
    polygon         US equities
    openfigi        Global identifier mapping
    stocksharp      Multi-exchange search

FAILOVER:
    The system supports automatic provider failover. Configure failover
    rules via the web API at /api/failover/config.

DETECTION:
    --detect-providers      Show available providers and their status
    --validate-credentials  Validate all configured API keys
    --test-connectivity     Test connections to all providers
");
    }

    internal static void ShowHelp()
    {
        Console.WriteLine(@"
╔══════════════════════════════════════════════════════════════════════╗
║                    Meridian v1.0                        ║
║          Real-time and historical market data collection             ║
╚══════════════════════════════════════════════════════════════════════╝

USAGE:
    Meridian [OPTIONS]
    Meridian --help <topic>

HELP TOPICS:
    --help backfill       Historical data backfill options
    --help symbols        Symbol management commands
    --help config         Configuration and environment variables
    --help package        Data packaging and import/export
    --help diagnostics    Diagnostics and troubleshooting
    --help providers      Data provider information

MODES:
    --mode <web|desktop|headless> Unified deployment mode selector
    --ui                    Start web dashboard (http://localhost:8080) [legacy]
    --backfill              Run historical data backfill
    --replay <path>         Replay events from JSONL file
    --package               Create a portable data package
    --import-package <path> Import a package into storage
    --list-package <path>   List contents of a package
    --validate-package <path> Validate a package
    --selftest              Run system self-tests
    --validate-config       Validate configuration without starting
    --dry-run               Comprehensive validation without starting (QW-93)
    --check-config          Alias for --dry-run --offline (config validation only, no network)
    --help, -h              Show this help message

AUTO-CONFIGURATION (First-time setup):
    --quickstart            Zero-config setup: auto-detect, validate, and configure (fastest)
    --wizard                Interactive configuration wizard (recommended for new users)
    --auto-config           Quick auto-configuration based on environment variables
    --detect-providers      Show available data providers and their status
    --validate-credentials  Validate configured API credentials
    --generate-config       Generate a configuration template
    --generate-config-schema Generate JSON Schema for appsettings.json

DIAGNOSTICS & TROUBLESHOOTING:
    --quick-check           Fast configuration health check
    --test-connectivity     Test connectivity to all configured providers
    --show-config           Display current configuration summary
    --error-codes           Show error code reference guide
    --check-schemas         Check stored data schema compatibility
    --simulate-feed         Emit a synthetic depth/trade event for smoke testing
    --recommend-providers   Print a scored provider recommendation report and exit

SCHEMA VALIDATION OPTIONS:
    --validate-schemas      Run schema check during startup
    --strict-schemas        Exit if schema incompatibilities found (use with --validate-schemas)
    --max-files <n>         Max files to check (default: 100, use with --check-schemas)
    --fail-fast             Stop on first incompatibility (use with --check-schemas)

SYMBOL MANAGEMENT:
    --symbols               Show all symbols (monitored + archived)
    --symbols-monitored     List symbols currently configured for monitoring
    --symbols-archived      List symbols with archived data files
    --symbols-add <list>    Add symbols to configuration (comma-separated)
    --symbols-remove <list> Remove symbols from configuration
    --symbol-status <sym>   Show detailed status for a specific symbol

SYMBOL OPTIONS (use with --symbols-add):
    --no-trades             Don't subscribe to trade data
    --no-depth              Don't subscribe to depth/L2 data
    --depth-levels <n>      Number of depth levels (default: 10)
    --update                Update existing symbols instead of skipping

OPTIONS:
    --config <path>         Path to configuration file (default: appsettings.json)
    --http-port <port>      HTTP server port (default: 8080)
    --watch-config          Enable hot-reload of configuration

ENVIRONMENT VARIABLES:
    MDC_CONFIG_PATH         Alternative to --config argument for specifying config path
    MDC_ENVIRONMENT         Environment name (e.g., Development, Production)
                            Loads appsettings.{Environment}.json as overlay
    DOTNET_ENVIRONMENT      Standard .NET environment variable (fallback for MDC_ENVIRONMENT)

BACKFILL OPTIONS:
    --backfill-provider <name>      Provider to use (default: stooq)
    --backfill-symbols <list>       Comma-separated symbols (e.g., AAPL,MSFT)
    --backfill-from <date>          Start date (YYYY-MM-DD)
    --backfill-to <date>            End date (YYYY-MM-DD)
    --resume                        Resume interrupted backfill from last checkpoint

PACKAGING OPTIONS:
    --package-name <name>           Package name (default: market-data-YYYYMMDD)
    --package-description <text>    Package description
    --package-output <path>         Output directory (default: packages)
    --package-symbols <list>        Comma-separated symbols to include
    --package-events <list>         Event types (Trade,BboQuote,L2Snapshot)
    --package-from <date>           Start date (YYYY-MM-DD)
    --package-to <date>             End date (YYYY-MM-DD)
    --package-format <fmt>          Format: zip, tar.gz (default: zip)
    --package-compression <level>   Compression: none, fast, balanced, max
    --no-quality-report             Exclude quality report from package
    --no-data-dictionary            Exclude data dictionary
    --no-loader-scripts             Exclude loader scripts
    --skip-checksums                Skip checksum verification

IMPORT OPTIONS:
    --import-destination <path>     Destination directory (default: data root)
    --skip-validation               Skip checksum validation during import
    --merge                         Merge with existing data (don't overwrite)

DATA QUERY & EXPORT TOOLS:
    --query <command>               Quick data query (last, count, summary, symbols, range)
    --generate-loader <tool>        Generate standalone loader scripts (python, r, pyarrow, postgresql, runmat)
    --output <path>                 Output directory for generated scripts
    --symbols <list>                Symbols filter (comma-separated)

AUTO-CONFIGURATION OPTIONS:
    --template <name>       Template for --generate-config: minimal, full, alpaca,
                            stocksharp, backfill, production, docker (default: minimal)
    --output <path>         Output path for generated config/schema
                            (config/appsettings.generated.json or config/appsettings.schema.json)

EXAMPLES:
    # Start web dashboard on default port
    Meridian --mode web

    # Start web dashboard on custom port
    Meridian --mode web --http-port 9000

    # Desktop mode (collector + UI server) with hot-reload
    Meridian --mode desktop --watch-config

    # Run historical backfill
    Meridian --backfill --backfill-symbols AAPL,MSFT,GOOGL \
        --backfill-from 2024-01-01 --backfill-to 2024-12-31

    # Run self-tests
    Meridian --selftest

    # Validate configuration without starting
    Meridian --validate-config

    # Validate a specific configuration file
    Meridian --validate-config --config /path/to/config.json

    # Create a portable data package
    Meridian --package --package-name my-data \
        --package-symbols AAPL,MSFT --package-from 2024-01-01

    # Create package with maximum compression
    Meridian --package --package-compression max

    # Import a package
    Meridian --import-package ./packages/my-data.zip

    # List package contents
    Meridian --list-package ./packages/my-data.zip

    # Validate a package
    Meridian --validate-package ./packages/my-data.zip

    # Run interactive configuration wizard (recommended for new users)
    Meridian --wizard

    # Quick auto-configuration based on environment variables
    Meridian --auto-config

    # Detect available data providers
    Meridian --detect-providers

    # Validate configured API credentials
    Meridian --validate-credentials

    # Generate a configuration template
    Meridian --generate-config --template alpaca --output config/appsettings.json

    # Quick configuration health check
    Meridian --quick-check

    # Test connectivity to all providers
    Meridian --test-connectivity

    # Show current configuration summary
    Meridian --show-config

    # View all error codes and their meanings
    Meridian --error-codes

    # Show all symbols (both monitored and archived)
    Meridian --symbols

    # Show only symbols currently being monitored
    Meridian --symbols-monitored

    # Show symbols that have archived data
    Meridian --symbols-archived

    # Add new symbols for monitoring
    Meridian --symbols-add AAPL,MSFT,GOOGL

    # Add symbols with custom options
    Meridian --symbols-add SPY,QQQ --no-depth --depth-levels 5

    # Remove symbols from monitoring
    Meridian --symbols-remove AAPL,MSFT

    # Check status of a specific symbol
    Meridian --symbol-status AAPL

    # Query stored data
    Meridian --query ""last SPY""
    Meridian --query ""count AAPL"" --from 2026-01-01

    # Generate loader scripts for Python, R, or RunMat
    Meridian --generate-loader python --output ./loaders
    Meridian --generate-loader r --symbols AAPL,MSFT
    Meridian --generate-loader runmat --symbols SPY,QQQ

CONFIGURATION:
    Configuration is loaded from appsettings.json by default, but can be customized:

    Priority for config file path:
      1. --config argument (highest priority)
      2. MDC_CONFIG_PATH environment variable
      3. appsettings.json (default)

    Environment-specific overlays:
      Set MDC_ENVIRONMENT=Production to automatically load appsettings.Production.json
      as an overlay on top of the base configuration.

    To get started:
      Copy appsettings.sample.json to appsettings.json and customize.

DATA PROVIDERS:
    - Interactive Brokers (IB): Level 2 market depth + trades
    - Alpaca: Real-time trades and quotes via WebSocket
    - Polygon: Real-time and historical data (coming soon)

DOCUMENTATION:
    For detailed documentation, see:
    - HELP.md                    - Complete user guide
    - README.md                  - Project overview
    - docs/CONFIGURATION.md      - Configuration reference
    - docs/GETTING_STARTED.md    - Setup guide
    - docs/TROUBLESHOOTING.md    - Common issues

SUPPORT:
    Report issues: https://github.com/rodoHasArrived/Test/issues
    Documentation: ./HELP.md

╔══════════════════════════════════════════════════════════════════════╗
║  QUICKSTART:   Run: ./Meridian --quickstart                ║
║  NEW USER?     Run: ./Meridian --wizard                   ║
║  QUICK CHECK:  Run: ./Meridian --quick-check              ║
║  START UI:     Run: ./Meridian --mode web                 ║
║  Then open http://localhost:8080 in your browser                     ║
╚══════════════════════════════════════════════════════════════════════╝
");
    }
}
