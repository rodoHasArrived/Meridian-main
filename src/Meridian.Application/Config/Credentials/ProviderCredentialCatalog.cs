using Meridian.Application.Config;
using Meridian.Contracts.Configuration;

namespace Meridian.Application.Config.Credentials;

public static class ProviderCredentialCatalog
{
    private static readonly IReadOnlyList<ProviderCredentialCatalogEntry> Entries =
    [
        new(
            ProviderId: "alpaca",
            DisplayName: "Alpaca",
            Capability: ProviderConnectionCapabilityDto.DataAndBrokerage,
            RequiredFields:
            [
                new ProviderCredentialFieldDefinition(
                    Name: "KeyId",
                    EnvironmentNames: [AlpacaCredentialEnvironment.KeyIdName, ..AlpacaCredentialEnvironment.KeyIdAliases]),
                new ProviderCredentialFieldDefinition(
                    Name: "SecretKey",
                    EnvironmentNames: [AlpacaCredentialEnvironment.SecretKeyName, ..AlpacaCredentialEnvironment.SecretKeyAliases])
            ],
            EnvironmentNames: [AlpacaCredentialEnvironment.TradingEnvironmentName],
            DefaultEnvironment: AlpacaCredentialEnvironment.PaperEnvironment,
            AffectedWorkflows:
            [
                "Trading readiness",
                "Portfolio brokerage sync",
                "Backfill fallback chain"
            ],
            RecommendedActionWhenMissing: "Add Alpaca paper API keys and verify /v2/account.",
            ActionHref: "/settings#alpaca-provider-setup"),
        new(
            ProviderId: "polygon",
            DisplayName: "Polygon.io",
            Capability: ProviderConnectionCapabilityDto.Data,
            RequiredFields:
            [
                new ProviderCredentialFieldDefinition("ApiKey", ["POLYGON_API_KEY"])
            ],
            AffectedWorkflows: ["Historical backfill", "Market data validation"],
            RecommendedActionWhenMissing: "Add the Polygon API key before routing data repair through Polygon."),
        new(
            ProviderId: "finnhub",
            DisplayName: "Finnhub",
            Capability: ProviderConnectionCapabilityDto.Data,
            RequiredFields:
            [
                new ProviderCredentialFieldDefinition("ApiKey", ["FINNHUB_API_KEY"])
            ],
            AffectedWorkflows: ["Historical backfill", "Market data validation"],
            RecommendedActionWhenMissing: "Add the Finnhub API key before enabling Finnhub as a repair source."),
        new(
            ProviderId: "tiingo",
            DisplayName: "Tiingo",
            Capability: ProviderConnectionCapabilityDto.Data,
            RequiredFields:
            [
                new ProviderCredentialFieldDefinition("ApiKey", ["TIINGO_API_TOKEN", "TIINGO_API_KEY"])
            ],
            AffectedWorkflows: ["Historical backfill", "Market data validation"],
            RecommendedActionWhenMissing: "Add the Tiingo token before using Tiingo for continuity repair."),
        new(
            ProviderId: "alphavantage",
            DisplayName: "Alpha Vantage",
            Capability: ProviderConnectionCapabilityDto.Data,
            RequiredFields:
            [
                new ProviderCredentialFieldDefinition("ApiKey", ["ALPHA_VANTAGE_API_KEY", "ALPHAVANTAGE_API_KEY"])
            ],
            AffectedWorkflows: ["Historical backfill", "Market data validation"],
            RecommendedActionWhenMissing: "Add the Alpha Vantage API key before using it as a fallback source."),
        new(
            ProviderId: "nasdaqdatalink",
            DisplayName: "Nasdaq Data Link",
            Capability: ProviderConnectionCapabilityDto.Data,
            RequiredFields:
            [
                new ProviderCredentialFieldDefinition("ApiKey", ["NASDAQ_DATA_LINK_API_KEY", "NASDAQ_API_KEY"])
            ],
            AffectedWorkflows: ["Reference data", "Historical backfill"],
            RecommendedActionWhenMissing: "Add the Nasdaq Data Link API key before routing reference-data repair."),
        new(
            ProviderId: "openfigi",
            DisplayName: "OpenFIGI",
            Capability: ProviderConnectionCapabilityDto.Data,
            RequiredFields:
            [
                new ProviderCredentialFieldDefinition("ApiKey", ["OPENFIGI_API_KEY"])
            ],
            AffectedWorkflows: ["Security master resolution", "Reference data"],
            RecommendedActionWhenMissing: "Add the OpenFIGI API key before enabling identifier repair."),
        new(
            ProviderId: "ib",
            DisplayName: "Interactive Brokers",
            Capability: ProviderConnectionCapabilityDto.DataAndBrokerage,
            RequiredFields: [],
            AffectedWorkflows: ["Trading readiness", "Brokerage sync", "Live market data"],
            RecommendedActionWhenMissing: "Confirm IB Gateway is available before routing live brokerage workflows."),
        new(
            ProviderId: "yahoo",
            DisplayName: "Yahoo Finance",
            Capability: ProviderConnectionCapabilityDto.Data,
            RequiredFields: [],
            AffectedWorkflows: ["Historical backfill fallback"],
            RecommendedActionWhenMissing: "No credential action required; monitor provider health before relying on fallback data."),
        new(
            ProviderId: "stooq",
            DisplayName: "Stooq",
            Capability: ProviderConnectionCapabilityDto.Data,
            RequiredFields: [],
            AffectedWorkflows: ["Historical backfill fallback"],
            RecommendedActionWhenMissing: "No credential action required; monitor data coverage before relying on fallback data."),
        new(
            ProviderId: "synthetic",
            DisplayName: "Synthetic",
            Capability: ProviderConnectionCapabilityDto.Data,
            RequiredFields: [],
            AffectedWorkflows: ["Offline simulation", "Demo mode"],
            RecommendedActionWhenMissing: "No credential action required for synthetic data.")
    ];

    private static readonly IReadOnlyDictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["alphaVantage"] = "alphavantage",
        ["alpha-vantage"] = "alphavantage",
        ["nasdaq"] = "nasdaqdatalink",
        ["nasdaq-data-link"] = "nasdaqdatalink",
        ["interactivebrokers"] = "ib",
        ["interactive-brokers"] = "ib"
    };

    public static IReadOnlyList<ProviderCredentialCatalogEntry> All => Entries;

    public static ProviderCredentialCatalogEntry? Find(string providerId)
    {
        var normalized = NormalizeProviderId(providerId);
        return Entries.FirstOrDefault(entry => string.Equals(entry.ProviderId, normalized, StringComparison.OrdinalIgnoreCase));
    }

    public static string NormalizeProviderId(string providerId)
    {
        var trimmed = (providerId ?? string.Empty).Trim();
        return Aliases.TryGetValue(trimmed, out var canonical) ? canonical : trimmed.ToLowerInvariant();
    }
}

public sealed record ProviderCredentialCatalogEntry(
    string ProviderId,
    string DisplayName,
    ProviderConnectionCapabilityDto Capability,
    IReadOnlyList<ProviderCredentialFieldDefinition> RequiredFields,
    IReadOnlyList<string>? EnvironmentNames = null,
    string? DefaultEnvironment = null,
    IReadOnlyList<string>? AffectedWorkflows = null,
    string RecommendedActionWhenMissing = "Add and verify credentials before using this provider for repair.",
    string? ActionHref = null)
{
    public bool RequiresCredentials => RequiredFields.Count > 0;

    public string ResolvedActionHref => ActionHref ?? $"/settings#provider-{ProviderId}-connection";

    public string NormalizeEnvironment(string? value)
        => ProviderId.Equals("alpaca", StringComparison.OrdinalIgnoreCase)
            ? AlpacaCredentialEnvironment.NormalizeTradingEnvironment(value)
            : string.IsNullOrWhiteSpace(value)
                ? DefaultEnvironment ?? string.Empty
                : value.Trim();
}

public sealed record ProviderCredentialFieldDefinition(
    string Name,
    IReadOnlyList<string> EnvironmentNames,
    bool Required = true);
