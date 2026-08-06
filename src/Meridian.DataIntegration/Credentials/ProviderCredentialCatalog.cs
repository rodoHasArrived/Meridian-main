using Meridian.Core.Config;
using Meridian.Contracts.Configuration;

namespace Meridian.DataIntegration.Credentials;

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
            ProviderId: "twelvedata",
            DisplayName: "Twelve Data",
            Capability: ProviderConnectionCapabilityDto.Data,
            RequiredFields:
            [
                new ProviderCredentialFieldDefinition("ApiKey", ["TWELVEDATA_API_KEY", "TWELVEDATA__APIKEY", "TWELVEDATA_APIKEY"])
            ],
            AffectedWorkflows: ["Historical backfill", "Symbol search", "Market data validation"],
            RecommendedActionWhenMissing: "Add the Twelve Data API key before routing backfill or symbol-search repair through Twelve Data."),
        new(
            ProviderId: "fred",
            DisplayName: "Federal Reserve Economic Data",
            Capability: ProviderConnectionCapabilityDto.Data,
            RequiredFields:
            [
                new ProviderCredentialFieldDefinition("ApiKey", ["FRED_API_KEY"])
            ],
            AffectedWorkflows: ["Economic reference data", "Historical backfill", "Symbol search"],
            RecommendedActionWhenMissing: "Add the FRED API key before routing economic-data lookup or backfill through FRED."),
        new(
            ProviderId: "robinhood",
            DisplayName: "Robinhood",
            Capability: ProviderConnectionCapabilityDto.DataAndBrokerage,
            RequiredFields:
            [
                new ProviderCredentialFieldDefinition("AccessToken", ["ROBINHOOD_ACCESS_TOKEN"])
            ],
            AffectedWorkflows: ["Trading readiness", "Brokerage sync", "Historical backfill", "Market data validation"],
            RecommendedActionWhenMissing: "Add the Robinhood access token before routing data or brokerage workflows through Robinhood."),
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
            ProviderId: "plaid",
            DisplayName: "Plaid",
            Capability: ProviderConnectionCapabilityDto.Data,
            RequiredFields:
            [
                new ProviderCredentialFieldDefinition("ClientId", ["PLAID_CLIENT_ID"]),
                new ProviderCredentialFieldDefinition("Secret", ["PLAID_SECRET", "PLAID_SANDBOX_SECRET", "PLAID_DEVELOPMENT_SECRET"])
            ],
            EnvironmentNames: ["PLAID_ENV", "PLAID_ENVIRONMENT"],
            DefaultEnvironment: "sandbox",
            AffectedWorkflows:
            [
                "Bank cash reconciliation",
                "Treasury account verification",
                "Investment account evidence",
                "Sandbox transfer authorization"
            ],
            RecommendedActionWhenMissing: "Add Plaid client credentials before linking bank accounts or syncing Plaid evidence.",
            ActionHref: "/settings#plaid-provider-setup"),
        new(
            ProviderId: "quickbooks-fixture",
            DisplayName: "QuickBooks Fixture",
            Capability: ProviderConnectionCapabilityDto.AccountingSystem,
            RequiredFields: [],
            AffectedWorkflows:
            [
                "External GL reconciliation",
                "Accounting records evidence",
                "Close-package trial balance review"
            ],
            RecommendedActionWhenMissing: "No credential action required; use the fixture to preview external GL reconciliation.",
            ActionHref: "/settings#provider-quickbooks-fixture-connection"),
        new(
            ProviderId: "quickbooks",
            DisplayName: "QuickBooks Online",
            Capability: ProviderConnectionCapabilityDto.AccountingSystem,
            RequiredFields:
            [
                new ProviderCredentialFieldDefinition("ClientId", ["QUICKBOOKS_CLIENT_ID", "QBO_CLIENT_ID"]),
                new ProviderCredentialFieldDefinition("ClientSecret", ["QUICKBOOKS_CLIENT_SECRET", "QBO_CLIENT_SECRET"]),
                new ProviderCredentialFieldDefinition("RefreshToken", ["QUICKBOOKS_REFRESH_TOKEN", "QBO_REFRESH_TOKEN"]),
                new ProviderCredentialFieldDefinition("RealmId", ["QUICKBOOKS_REALM_ID", "QBO_REALM_ID"]),
                new ProviderCredentialFieldDefinition("CompanyName", ["QUICKBOOKS_COMPANY_NAME", "QBO_COMPANY_NAME"], Required: false)
            ],
            EnvironmentNames: ["QUICKBOOKS_ENVIRONMENT"],
            DefaultEnvironment: "sandbox",
            AffectedWorkflows:
            [
                "External GL reconciliation",
                "Accounting records evidence",
                "Close-package trial balance review"
            ],
            RecommendedActionWhenMissing: "Add QuickBooks Online OAuth client ID, client secret, refresh token, and company realm ID before importing read-only GL evidence.",
            ActionHref: "/settings#provider-quickbooks-connection"),
        new(
            ProviderId: "ib-flex",
            DisplayName: "Interactive Brokers Flex Web Service",
            Capability: ProviderConnectionCapabilityDto.DataAndBrokerage,
            RequiredFields:
            [
                new ProviderCredentialFieldDefinition("Token", ["IB_FLEX_TOKEN", "IBKR_FLEX_TOKEN"]),
                new ProviderCredentialFieldDefinition("QueryId", ["IB_FLEX_QUERY_ID", "IBKR_FLEX_QUERY_ID"])
            ],
            AffectedWorkflows:
            [
                "Scheduled broker statement import",
                "Margin and cash reconciliation",
                "Tax-lot and options lifecycle evidence"
            ],
            RecommendedActionWhenMissing: "Create an IB Activity Flex Query, enable the required sections, and add its token and query ID.",
            ActionHref: "/settings#provider-ib-flex-connection"),
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
        ["alpaca-brokerage"] = "alpaca",
        ["alpaca-corp-actions"] = "alpaca",
        ["alpaca-options"] = "alpaca",
        ["alphaVantage"] = "alphavantage",
        ["alpha-vantage"] = "alphavantage",
        ["ibflex"] = "ib-flex",
        ["ib-flex-web-service"] = "ib-flex",
        ["nasdaq"] = "nasdaqdatalink",
        ["nasdaq-corp-actions"] = "nasdaqdatalink",
        ["nasdaq-data-link"] = "nasdaqdatalink",
        ["nasdaq-symbols"] = "nasdaqdatalink",
        ["polygon-options"] = "polygon",
        ["fred-symbols"] = "fred",
        ["robinhood-brokerage"] = "robinhood",
        ["robinhood-live"] = "robinhood",
        ["robinhood-options"] = "robinhood",
        ["robinhood-symbols"] = "robinhood",
        ["tiingo-corp-actions"] = "tiingo",
        ["tiingo-symbols"] = "tiingo",
        ["twelve-data"] = "twelvedata",
        ["twelve_data"] = "twelvedata",
        ["twelveData"] = "twelvedata",
        ["twelvedata-corp-actions"] = "twelvedata",
        ["twelvedata-symbols"] = "twelvedata",
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

    public static IReadOnlyList<ProviderCredentialFieldMetadataDto> BuildCredentialFields(ProviderCredentialCatalogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return entry.RequiredFields
            .Select(field => new ProviderCredentialFieldMetadataDto(
                field.Name,
                LabelCredentialField(field.Name),
                field.Required,
                ResolveCredentialInputKind(field.Name),
                ResolveCredentialPlaceholder(field),
                ResolveCredentialHelpText(entry.ProviderId, field)))
            .ToArray();
    }

    public static IReadOnlyList<ProviderEnvironmentOptionDto> BuildEnvironmentOptions(ProviderCredentialCatalogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var environments = ResolveAllowedEnvironments(entry);
        if (environments.Count == 0)
        {
            return [];
        }

        var defaultEnvironment = entry.NormalizeEnvironment(entry.DefaultEnvironment);
        return environments
            .Select(environment =>
            {
                var normalized = entry.NormalizeEnvironment(environment);
                return new ProviderEnvironmentOptionDto(
                    normalized,
                    LabelEnvironment(normalized),
                    string.Equals(normalized, defaultEnvironment, StringComparison.OrdinalIgnoreCase),
                    ResolveEnvironmentHelpText(entry.ProviderId, normalized));
            })
            .GroupBy(option => option.Value, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    private static IReadOnlyList<string> ResolveAllowedEnvironments(ProviderCredentialCatalogEntry entry)
        => entry.ProviderId.ToLowerInvariant() switch
        {
            "alpaca" => [AlpacaCredentialEnvironment.PaperEnvironment, AlpacaCredentialEnvironment.LiveEnvironment],
            "plaid" => ["sandbox", "development", "production"],
            "quickbooks" => ["sandbox", "production"],
            _ when !string.IsNullOrWhiteSpace(entry.DefaultEnvironment) => [entry.DefaultEnvironment],
            _ => []
        };

    private static ProviderCredentialInputKindDto ResolveCredentialInputKind(string fieldName)
    {
        if (fieldName.Contains("Url", StringComparison.OrdinalIgnoreCase) ||
            fieldName.Contains("Endpoint", StringComparison.OrdinalIgnoreCase))
        {
            return ProviderCredentialInputKindDto.Url;
        }

        if (fieldName.Equals("RealmId", StringComparison.OrdinalIgnoreCase) ||
            fieldName.Equals("CompanyName", StringComparison.OrdinalIgnoreCase))
        {
            return ProviderCredentialInputKindDto.Text;
        }

        return ProviderCredentialInputKindDto.Password;
    }

    private static string LabelCredentialField(string fieldName)
        => fieldName switch
        {
            "ApiKey" => "API key",
            "KeyId" => "Key ID",
            "SecretKey" => "Secret key",
            "ClientId" => "Client ID",
            "ClientSecret" => "Client secret",
            "RefreshToken" => "Refresh token",
            "RealmId" => "Company realm ID",
            "CompanyName" => "Company name",
            "Secret" => "Secret",
            _ => SplitPascalCase(fieldName)
        };

    private static string? ResolveCredentialPlaceholder(ProviderCredentialFieldDefinition field)
        => field.EnvironmentNames.FirstOrDefault()
           ?? (field.Required ? "Stored server-side and masked after save" : "Optional");

    private static string ResolveCredentialHelpText(string providerId, ProviderCredentialFieldDefinition field)
        => ProviderCredentialCatalog.NormalizeProviderId(providerId) switch
        {
            "alpaca" => "Stored in Meridian's encrypted local provider store for Alpaca account verification.",
            "plaid" => "Used server-side to create Plaid link tokens and retain bank evidence.",
            "quickbooks" => field.Name switch
            {
                "ClientId" => "Stored in Meridian's encrypted local provider store for OAuth token refresh.",
                "ClientSecret" => "Used only server-side for OAuth token refresh.",
                "RefreshToken" => "Token exchange refreshes read-only API access and stores rotated tokens locally.",
                "RealmId" => "Selects the QuickBooks Online company to read.",
                "CompanyName" => "Optional display label for the selected QuickBooks company.",
                _ => "Stored in Meridian's encrypted local provider store and masked after save."
            },
            _ when field.Required => "Stored in Meridian's encrypted local provider store and masked after save.",
            _ => "Optional provider metadata; no secret value is displayed after save."
        };

    private static string LabelEnvironment(string value)
        => value switch
        {
            "paper" => "Paper",
            "live" => "Live",
            "sandbox" => "Sandbox",
            "development" => "Development",
            "production" => "Production",
            _ => SplitPascalCase(value)
        };

    private static string ResolveEnvironmentHelpText(string providerId, string environment)
        => ProviderCredentialCatalog.NormalizeProviderId(providerId) switch
        {
            "alpaca" when environment.Equals(AlpacaCredentialEnvironment.LiveEnvironment, StringComparison.OrdinalIgnoreCase)
                => "Live Alpaca credentials remain gated by live-routing acknowledgement and execution controls.",
            "alpaca" => "Paper is the default Alpaca environment for setup and verification.",
            "plaid" when environment.Equals("production", StringComparison.OrdinalIgnoreCase)
                => "Production Plaid credentials are used only for server-owned link and evidence sync flows.",
            "plaid" => "Plaid sandbox and development environments support institution linking and evidence tests.",
            "quickbooks" when environment.Equals("production", StringComparison.OrdinalIgnoreCase)
                => "Production QuickBooks Online access remains read-only GL evidence import.",
            "quickbooks" => "QuickBooks sandbox is the default for read-only GL evidence setup.",
            _ => "Provider environment selected for server-side credential verification."
        };

    private static string SplitPascalCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Credential";
        }

        var chars = new List<char>(value.Length + 8);
        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            if (index > 0 && char.IsUpper(current) && !char.IsWhiteSpace(value[index - 1]))
            {
                chars.Add(' ');
            }

            chars.Add(current);
        }

        return new string(chars.ToArray());
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
