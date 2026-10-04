using System.Collections.Frozen;

namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>
/// Canonical provider-family identities shared by discovery, configuration, routing and telemetry.
/// Unknown plugin IDs remain valid and are normalized by trimming and invariant lower-casing.
/// </summary>
public static class ProviderIdentity
{
    /// <summary>
    /// Built-in adapter families, including families that do not implement production provider
    /// contracts. An identity alone never grants a runtime registration or capability.
    /// </summary>
    public static IReadOnlySet<string> CanonicalFamilyIds { get; } = new[]
    {
        "alpaca", "alphavantage", "edgar", "finnhub", "fred", "ibkr", "nasdaq", "nyse",
        "openfigi", "plaid", "polygon", "robinhood", "stooq", "synthetic", "templates",
        "tiingo", "tradestation", "tradier", "twelvedata", "yahoo"
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// Accepted legacy configuration and capability IDs mapped directly to their canonical ID.
    /// Entries are explicit: suffixes are never stripped from unknown plugin identifiers.
    /// The IB Flex credential resource and IB simulation transport retain their distinct IDs;
    /// resolving those names as ibkr would change the requested integration or execution mode.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Aliases { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["alpaca-api"] = "alpaca",
            ["alpaca-brokerage"] = "alpaca",
            ["alpaca-corp-actions"] = "alpaca",
            ["alpaca-options"] = "alpaca",
            ["alpaca-symbols"] = "alpaca",
            ["alpha-vantage"] = "alphavantage",
            ["alpha_vantage"] = "alphavantage",
            ["alphavantage-corp-actions"] = "alphavantage",
            ["alphavantage-symbols"] = "alphavantage",
            ["edgar-symbols"] = "edgar",
            ["finnhub-corp-actions"] = "finnhub",
            ["finnhub-symbols"] = "finnhub",
            ["fred-symbols"] = "fred",
            ["ib"] = "ibkr",
            ["interactivebrokers"] = "ibkr",
            ["interactive-brokers"] = "ibkr",
            ["interactive_brokers"] = "ibkr",
            ["ibflex"] = "ib-flex",
            ["ib-flex-web-service"] = "ib-flex",
            ["nasdaqdatalink"] = "nasdaq",
            ["nasdaq-data-link"] = "nasdaq",
            ["nasdaq_data_link"] = "nasdaq",
            ["nasdaq-corp-actions"] = "nasdaq",
            ["nasdaq-symbols"] = "nasdaq",
            ["nyse-streaming"] = "nyse",
            ["open-figi"] = "openfigi",
            ["openfigi-api"] = "openfigi",
            ["plaid-api"] = "plaid",
            ["polygon-options"] = "polygon",
            ["polygonio"] = "polygon",
            ["polygon-io"] = "polygon",
            ["polygon-symbols"] = "polygon",
            ["robinhood-brokerage"] = "robinhood",
            ["robinhood-live"] = "robinhood",
            ["robinhood-options"] = "robinhood",
            ["robinhood-symbols"] = "robinhood",
            ["synthetic-options"] = "synthetic",
            ["template-brokerage"] = "templates",
            ["tiingo-corp-actions"] = "tiingo",
            ["tiingo-symbols"] = "tiingo",
            ["twelve-data"] = "twelvedata",
            ["twelve_data"] = "twelvedata",
            ["twelvedata-corp-actions"] = "twelvedata",
            ["twelvedata-symbols"] = "twelvedata",
            ["twelvedata-api"] = "twelvedata",
            ["yahoofinance"] = "yahoo",
            ["yahoo-finance"] = "yahoo",
            ["qbo"] = "quickbooks",
            ["quickbooks-online"] = "quickbooks",
            ["qbo-fixture"] = "quickbooks-fixture"
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    public static string NormalizeId(string providerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        var trimmed = providerId.Trim();
        return Aliases.TryGetValue(trimmed, out var canonical) ? canonical : trimmed.ToLowerInvariant();
    }

    public static bool EqualsId(string? left, string? right)
        => !string.IsNullOrWhiteSpace(left) &&
           !string.IsNullOrWhiteSpace(right) &&
           string.Equals(NormalizeId(left), NormalizeId(right), StringComparison.Ordinal);
}
