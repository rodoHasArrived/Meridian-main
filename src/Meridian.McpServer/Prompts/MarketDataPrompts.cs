using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;

namespace Meridian.McpServer.Prompts;

/// <summary>
/// MCP prompt templates for common Meridian workflows.
/// These guide an LLM through multi-step operations such as configuring a provider
/// or setting up a historical data backfill.
/// </summary>
[McpServerPromptType]
public sealed class MarketDataPrompts
{
    /// <summary>
    /// Returns a guided prompt for setting up a historical data backfill.
    /// </summary>
    [McpServerPrompt, Description(
        "Generate a step-by-step guide for running a historical data backfill " +
        "for one or more symbols using a chosen provider.")]
    public static IEnumerable<ChatMessage> BackfillSetup(
        [Description("Comma-separated ticker symbols to backfill (e.g. \"SPY,AAPL\").")]
        string symbols = "SPY",
        [Description("Historical data provider (e.g. \"stooq\", \"yahoo\", \"tiingo\"). " +
                     "Call list_backfill_providers to see all options.")]
        string provider = "stooq",
        [Description("Start date in yyyy-MM-dd format (optional).")]
        string? from = null,
        [Description("End date in yyyy-MM-dd format (optional).")]
        string? to = null)
    {
        var dateRange = (from, to) switch
        {
            (not null, not null) => $"from {from} to {to}",
            (not null, null) => $"from {from} to today",
            (null, not null) => $"up to {to}",
            _ => "for all available history"
        };

        yield return new ChatMessage(ChatRole.User,
            $"Help me backfill historical data for {symbols} using the {provider} provider {dateRange}.");

        yield return new ChatMessage(ChatRole.Assistant, $"""
            Here is how to backfill historical data for **{symbols}** using **{provider}** {dateRange}:

            ## Step 1 — Verify the provider is available
            Call the `list_backfill_providers` tool to confirm that `{provider}` is registered
            and check its capabilities (adjusted prices, dividends, rate limits, etc.).

            ## Step 2 — Run the backfill
            Call the `run_backfill` tool with these parameters:
            - `symbols`: `{symbols}`
            - `provider`: `{provider}`
            {(from is not null ? $"- `from`: `{from}`" : "- `from`: _(omit for provider default)_")}
            {(to is not null ? $"- `to`: `{to}`" : "- `to`:   _(omit for today)_")}

            ## Step 3 — Review the result
            The tool returns a JSON object with:
            - `success` — whether the backfill completed without errors
            - `barsWritten` — number of OHLCV bars written to storage
            - `durationSeconds` — elapsed time
            - `error` — error message if `success` is false

            ## Step 4 — Check what was stored
            Call `list_stored_symbols` to confirm the data appears in the storage catalog,
            then use `query_stored_data` to inspect individual records.

            ## Troubleshooting
            - **Provider unavailable**: check credentials in `config/appsettings.json`
            - **Zero bars written**: the symbol may not be listed on this provider's exchange
            - **Rate limit error**: reduce concurrency or add a delay between requests
            """);
    }

    /// <summary>
    /// Returns a guided prompt for configuring a new data provider.
    /// </summary>
    [McpServerPrompt, Description(
        "Generate a step-by-step guide for configuring a new data provider " +
        "in the Meridian.")]
    public static IEnumerable<ChatMessage> ProviderConfiguration(
        [Description("Provider identifier (e.g. \"alpaca\", \"polygon\", \"tiingo\").")]
        string provider = "alpaca")
    {
        yield return new ChatMessage(ChatRole.User,
            $"How do I configure the {provider} provider in Meridian?");

        yield return new ChatMessage(ChatRole.Assistant, $"""
            Here is how to configure the **{provider}** provider:

            ## Step 1 — Obtain API credentials
            Visit the {provider} developer portal and create an API key.
            Store the key in an **environment variable** — never hard-code it:
            ```bash
            export {provider.ToUpperInvariant()}_API_KEY=your-key-here
            # Some providers also need a secret:
            export {provider.ToUpperInvariant()}_SECRET_KEY=your-secret-here
            ```

            ## Step 2 — Update appsettings.json
            Open `config/appsettings.json` and set the active data source:
            ```json
            {{
              "DataSource": "{provider}",
              "{provider}": {{
                "Enabled": true,
                "ApiKey": "${{env:{provider.ToUpperInvariant()}_API_KEY}}"
              }}
            }}
            ```

            ## Step 3 — Add symbols to collect
            Use the `add_symbol` tool to register the ticker symbols you want,
            or edit the `Symbols` array in `config/appsettings.json` directly.

            ## Step 4 — Verify connectivity
            Run the application with `--quick-check` to validate credentials:
            ```bash
            dotnet run --project src/Meridian -- --quick-check
            ```

            ## Step 5 — Start collection
            ```bash
            dotnet run --project src/Meridian
            ```

            ## Notes
            - Call `list_backfill_providers` to see which providers support historical data.
            - Call `list_configured_symbols` to verify your symbol list.
            - Refer to `docs/providers/` for provider-specific setup guides.
            """);
    }
}
