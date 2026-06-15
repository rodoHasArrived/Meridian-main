using Meridian.Core.Config;
using Meridian.Contracts.Configuration;

namespace Meridian.DataIntegration.Credentials;

public static class ProviderCredentialCatalog
{
    private static readonly IReadOnlyList<ProviderCredentialCatalogEntry> Entries =
    [
        new(
            ProviderId: "alpaca",
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
            DefaultEnvironment: AlpacaCredentialEnvironment.PaperEnvironment),
        new(
            ProviderId: "polygon",
            RequiredFields:
            [
                new ProviderCredentialFieldDefinition("ApiKey", ["POLYGON_API_KEY"])
            ]),
        new(
            ProviderId: "finnhub",
            RequiredFields:
            [
                new ProviderCredentialFieldDefinition("ApiKey", ["FINNHUB_API_KEY"])
            ]),
        new(
            ProviderId: "tiingo",
            RequiredFields:
            [
                new ProviderCredentialFieldDefinition("ApiKey", ["TIINGO_API_TOKEN", "TIINGO_API_KEY"])
            ]),
        new(
            ProviderId: "alphavantage",
            RequiredFields:
            [
                new ProviderCredentialFieldDefinition("ApiKey", ["ALPHA_VANTAGE_API_KEY", "ALPHAVANTAGE_API_KEY"])
            ]),
        new(
            ProviderId: "nasdaqdatalink",
            RequiredFields:
            [
                new ProviderCredentialFieldDefinition("ApiKey", ["NASDAQ_DATA_LINK_API_KEY", "NASDAQ_API_KEY"])
            ]),
        new(
            ProviderId: "openfigi",
            RequiredFields:
            [
                new ProviderCredentialFieldDefinition("ApiKey", ["OPENFIGI_API_KEY"])
            ]),
        new(
            ProviderId: "plaid",
            RequiredFields:
            [
                new ProviderCredentialFieldDefinition("ClientId", ["PLAID_CLIENT_ID"]),
                new ProviderCredentialFieldDefinition("Secret", ["PLAID_SECRET", "PLAID_SANDBOX_SECRET", "PLAID_DEVELOPMENT_SECRET"])
            ],
            EnvironmentNames: ["PLAID_ENV", "PLAID_ENVIRONMENT"],
            DefaultEnvironment: "sandbox"),
        new(
            ProviderId: "quickbooks-fixture",
            RequiredFields: []),
        new(
            ProviderId: "quickbooks",
            RequiredFields:
            [
                new ProviderCredentialFieldDefinition("ClientId", ["QUICKBOOKS_CLIENT_ID", "QBO_CLIENT_ID"]),
                new ProviderCredentialFieldDefinition("ClientSecret", ["QUICKBOOKS_CLIENT_SECRET", "QBO_CLIENT_SECRET"]),
                new ProviderCredentialFieldDefinition("RefreshToken", ["QUICKBOOKS_REFRESH_TOKEN", "QBO_REFRESH_TOKEN"]),
                new ProviderCredentialFieldDefinition("RealmId", ["QUICKBOOKS_REALM_ID", "QBO_REALM_ID"]),
                new ProviderCredentialFieldDefinition("CompanyName", ["QUICKBOOKS_COMPANY_NAME", "QBO_COMPANY_NAME"], Required: false)
            ],
            EnvironmentNames: ["QUICKBOOKS_ENVIRONMENT"],
            DefaultEnvironment: "sandbox"),
        new(
            ProviderId: "ib",
            RequiredFields: []),
        new(
            ProviderId: "yahoo",
            RequiredFields: []),
        new(
            ProviderId: "stooq",
            RequiredFields: []),
        new(
            ProviderId: "synthetic",
            RequiredFields: [])
    ];

    private static readonly IReadOnlyDictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["alphaVantage"] = "alphavantage",
        ["alpha-vantage"] = "alphavantage",
        ["nasdaq"] = "nasdaqdatalink",
        ["nasdaq-data-link"] = "nasdaqdatalink",
        ["interactivebrokers"] = "ib",
        ["interactive-brokers"] = "ib",
        ["plaid-api"] = "plaid",
        ["qbo"] = "quickbooks",
        ["quickbooks-online"] = "quickbooks",
        ["qbo-fixture"] = "quickbooks-fixture"
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
    IReadOnlyList<ProviderCredentialFieldDefinition> RequiredFields,
    IReadOnlyList<string>? EnvironmentNames = null,
    string? DefaultEnvironment = null)
{
    public bool RequiresCredentials => RequiredFields.Count > 0;

    public string NormalizeEnvironment(string? value)
    {
        if (ProviderId.Equals("alpaca", StringComparison.OrdinalIgnoreCase))
        {
            return AlpacaCredentialEnvironment.NormalizeTradingEnvironment(value);
        }

        if (ProviderId.Equals("plaid", StringComparison.OrdinalIgnoreCase))
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? DefaultEnvironment ?? "sandbox" : value.Trim().ToLowerInvariant();
            return normalized is "production" or "development" or "sandbox" ? normalized : "sandbox";
        }

        if (ProviderId.Equals("quickbooks", StringComparison.OrdinalIgnoreCase))
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? DefaultEnvironment ?? "sandbox" : value.Trim().ToLowerInvariant();
            return normalized is "production" or "live" ? "production" : "sandbox";
        }

        return string.IsNullOrWhiteSpace(value) ? DefaultEnvironment ?? string.Empty : value.Trim();
    }
}

public sealed record ProviderCredentialFieldDefinition(
    string Name,
    IReadOnlyList<string> EnvironmentNames,
    bool Required = true);
