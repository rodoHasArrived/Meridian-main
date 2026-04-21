using Meridian.Application.Composition;
using Meridian.McpServer.Navigation;
using Meridian.McpServer.Prompts;
using Meridian.McpServer.Resources;
using Meridian.McpServer.Tools;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Serilog;

// ─────────────────────────────────────────────────────────────────────────────
// Meridian — MCP Server
//
// Exposes market data capabilities as Model Context Protocol (MCP) tools,
// resources, and prompts so that LLMs can query providers, run backfills,
// inspect the storage catalog, and manage symbols.
//
// Transport: stdio (default) — connect via any MCP-compliant client.
//
// Config path precedence:
//   1. --config <path> CLI argument
//   2. MDC_CONFIG_PATH environment variable
//   3. Default: config/appsettings.json
// ─────────────────────────────────────────────────────────────────────────────

// Early Serilog bootstrap so MCP-level diagnostics are visible on stderr
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Warning()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
                     standardErrorFromLevel: Serilog.Events.LogEventLevel.Verbose)
    .CreateLogger();

try
{
    var configPath = args.SkipWhile(a => a != "--config").Skip(1).FirstOrDefault()
        ?? Environment.GetEnvironmentVariable("MDC_CONFIG_PATH")
        ?? "config/appsettings.json";

    var builder = Host.CreateApplicationBuilder(args);

    // Wire Serilog as the Microsoft.Extensions.Logging provider so that the
    // Application layer's structured logging flows to stderr (not stdout, which
    // is reserved for the MCP stdio transport).
    builder.Services.AddLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddSerilog(Log.Logger, dispose: false);
    });

    // Register all Meridian services using the McpServer preset,
    // which enables providers + backfill but skips the streaming pipeline and
    // collector (not needed for a query-oriented MCP server).
    builder.Services.AddMarketDataServices(CompositionOptions.McpServer with
    {
        ConfigPath = configPath
    });
    builder.Services.AddSingleton<RepoNavigationCatalog>();

    // Register the MCP server with stdio transport and all tool/resource/prompt types.
    builder.Services.AddMcpServer()
        .WithStdioServerTransport()
        .WithTools<ProviderTools>()
        .WithTools<BackfillTools>()
        .WithTools<StorageTools>()
        .WithTools<SymbolTools>()
        .WithTools<RepoNavigationTools>()
        .WithResources<MarketDataResources>()
        .WithResources<RepoNavigationResources>()
        .WithPrompts<MarketDataPrompts>();

    await builder.Build().RunAsync();
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Meridian.McpServer terminated unexpectedly");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}
