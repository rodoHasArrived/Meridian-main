using Meridian.Core.Config;
using Meridian.Contracts.Configuration;

namespace Meridian.DataIntegration.Credentials;

public static class ProviderSetupExperienceCatalog
{
    private static readonly IReadOnlyList<ProviderSetupExperienceCatalogEntry> Entries =
    [
        new("alpaca", "Alpaca", ProviderConnectionCapabilityDto.DataAndBrokerage, "Brokerage and market data", "Connect Alpaca paper trading for account verification and market-data routing.", "Use paper keys first. Live credentials remain gated by verification and execution controls.", ["Create or open an Alpaca paper account.", "Copy the API key ID and secret key into Meridian.", "Verify /v2/account before routing brokerage workflows."], "Live Alpaca credentials must remain gated until certification passes.", ["Trading readiness", "Portfolio brokerage sync", "Backfill fallback chain"], ["docs/operators/README.md"], "Add Alpaca paper API keys and verify /v2/account.", "/settings#alpaca-provider-setup", "Verify Alpaca credentials"),
        new("polygon", "Polygon.io", ProviderConnectionCapabilityDto.Data, "Market data", "Configure Polygon for historical repair and market-data validation.", "Store the Polygon API key in Meridian's encrypted provider vault.", ["Paste the Polygon API key.", "Save credentials.", "Run provider verification before using Polygon for repair."], null, ["Historical backfill", "Market data validation"], ["docs/operators/README.md"], "Add the Polygon API key before routing data repair through Polygon.", null, "Verify Polygon credentials"),
        new("finnhub", "Finnhub", ProviderConnectionCapabilityDto.Data, "Market data", "Configure Finnhub as a continuity and validation data source.", "Store the Finnhub API key in Meridian's encrypted provider vault.", ["Paste the Finnhub API key.", "Save credentials.", "Review provider health before enabling repair routing."], null, ["Historical backfill", "Market data validation"], ["docs/operators/README.md"], "Add the Finnhub API key before enabling Finnhub as a repair source.", null, "Verify Finnhub credentials"),
        new("tiingo", "Tiingo", ProviderConnectionCapabilityDto.Data, "Market data", "Configure Tiingo as a continuity repair source.", "Store the Tiingo token in Meridian's encrypted provider vault.", ["Paste the Tiingo token.", "Save credentials.", "Review data coverage before enabling fallback routing."], null, ["Historical backfill", "Market data validation"], ["docs/operators/README.md"], "Add the Tiingo token before using Tiingo for continuity repair.", null, "Verify Tiingo credentials"),
        new("alphavantage", "Alpha Vantage", ProviderConnectionCapabilityDto.Data, "Market data", "Configure Alpha Vantage as a fallback market-data source.", "Store the Alpha Vantage API key in Meridian's encrypted provider vault.", ["Paste the Alpha Vantage API key.", "Save credentials.", "Confirm fallback routing limits before use."], null, ["Historical backfill", "Market data validation"], ["docs/operators/README.md"], "Add the Alpha Vantage API key before using it as a fallback source.", null, "Verify Alpha Vantage credentials"),
        new("nasdaqdatalink", "Nasdaq Data Link", ProviderConnectionCapabilityDto.Data, "Reference data", "Configure Nasdaq Data Link for reference-data and historical repair.", "Store the Nasdaq Data Link API key in Meridian's encrypted provider vault.", ["Paste the Nasdaq Data Link API key.", "Save credentials.", "Review reference-data routing before use."], null, ["Reference data", "Historical backfill"], ["docs/operators/README.md"], "Add the Nasdaq Data Link API key before routing reference-data repair.", null, "Verify Nasdaq Data Link credentials"),
        new("openfigi", "OpenFIGI", ProviderConnectionCapabilityDto.Data, "Reference data", "Configure OpenFIGI for security-master identifier repair.", "Store the OpenFIGI API key in Meridian's encrypted provider vault.", ["Paste the OpenFIGI API key.", "Save credentials.", "Review identifier repair readiness."], null, ["Security master resolution", "Reference data"], ["docs/operators/README.md"], "Add the OpenFIGI API key before enabling identifier repair.", null, "Verify OpenFIGI credentials"),
        new("plaid", "Plaid", ProviderConnectionCapabilityDto.Data, "Bank connectivity", "Link bank and investment accounts for retained cash and evidence workflows.", "Plaid credentials are used server-side for link-token creation and evidence sync.", ["Select the Plaid environment.", "Save client ID and secret.", "Open Link to connect institutions."], "Production Plaid credentials should only be used for governed evidence sync flows.", ["Bank cash reconciliation", "Treasury account verification", "Investment account evidence", "Sandbox transfer authorization"], ["docs/operators/README.md"], "Add Plaid client credentials before linking bank accounts or syncing Plaid evidence.", "/settings#plaid-provider-setup", "Open Plaid Link"),
        new("quickbooks-fixture", "QuickBooks Fixture", ProviderConnectionCapabilityDto.AccountingSystem, "Accounting systems", "Preview external GL reconciliation with the built-in fixture.", "No credential action is required for fixture evidence.", ["Enable the fixture connection.", "Import trial-balance evidence.", "Compare fixture results in reconciliation views."], null, ["External GL reconciliation", "Accounting records evidence", "Close-package trial balance review"], ["docs/operators/README.md"], "No credential action required; use the fixture to preview external GL reconciliation.", "/settings#provider-quickbooks-fixture-connection", "Open fixture connection"),
        new("quickbooks", "QuickBooks Online", ProviderConnectionCapabilityDto.AccountingSystem, "Accounting systems", "Connect QuickBooks Online for read-only GL evidence import.", "OAuth values are stored server-side and used only for read-only accounting evidence import.", ["Select sandbox or production.", "Save OAuth client, refresh token, realm, and optional company label.", "Verify read-only company access before importing GL evidence."], "Posting and export remain disabled; QuickBooks access is read-only.", ["External GL reconciliation", "Accounting records evidence", "Close-package trial balance review"], ["docs/operators/README.md"], "Add QuickBooks Online OAuth client ID, client secret, refresh token, and company realm ID before importing read-only GL evidence.", "/settings#provider-quickbooks-connection", "Verify QuickBooks connection"),
        new("ib", "Interactive Brokers", ProviderConnectionCapabilityDto.DataAndBrokerage, "Brokerage and market data", "Connect Interactive Brokers Gateway for brokerage and market-data workflows.", "Confirm Gateway availability before routing live workflows.", ["Start IB Gateway.", "Confirm endpoint settings.", "Verify provider health before routing workflows."], "Live brokerage routing remains gated by execution controls.", ["Trading readiness", "Brokerage sync", "Live market data"], ["docs/operators/README.md"], "Confirm IB Gateway is available before routing live brokerage workflows.", null, "Check IB Gateway"),
        new("yahoo", "Yahoo Finance", ProviderConnectionCapabilityDto.Data, "Fallback data", "Use Yahoo Finance as a no-credential historical fallback.", "Monitor coverage before relying on fallback data.", ["Enable the provider.", "Review health and coverage.", "Use as a fallback only when primary sources are unavailable."], null, ["Historical backfill fallback"], ["docs/operators/README.md"], "No credential action required; monitor provider health before relying on fallback data.", null, "Review Yahoo health"),
        new("stooq", "Stooq", ProviderConnectionCapabilityDto.Data, "Fallback data", "Use Stooq as a no-credential historical fallback.", "Monitor data coverage before relying on fallback data.", ["Enable the provider.", "Review health and coverage.", "Use as a fallback only when primary sources are unavailable."], null, ["Historical backfill fallback"], ["docs/operators/README.md"], "No credential action required; monitor data coverage before relying on fallback data.", null, "Review Stooq health"),
        new("synthetic", "Synthetic", ProviderConnectionCapabilityDto.Data, "Simulation", "Use deterministic synthetic data for offline simulation and demos.", "Synthetic data is not market evidence.", ["Select synthetic data.", "Run offline workflows.", "Do not use synthetic rows as retained source evidence."], null, ["Offline simulation", "Demo mode"], ["docs/operators/README.md"], "No credential action required for synthetic data.", null, "Open synthetic setup")
    ];

    public static ProviderSetupExperienceCatalogEntry Find(string providerId)
        => Entries.FirstOrDefault(entry => string.Equals(entry.ProviderId, ProviderCredentialCatalog.NormalizeProviderId(providerId), StringComparison.OrdinalIgnoreCase))
           ?? ProviderSetupExperienceCatalogEntry.Default(ProviderCredentialCatalog.NormalizeProviderId(providerId));

    public static IReadOnlyList<ProviderCredentialFieldMetadataDto> BuildCredentialFields(ProviderCredentialCatalogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.RequiredFields.Select(field => new ProviderCredentialFieldMetadataDto(field.Name, LabelCredentialField(field.Name), field.Required, ResolveCredentialInputKind(field.Name), field.EnvironmentNames.FirstOrDefault() ?? (field.Required ? "Stored server-side and masked after save" : "Optional"), ResolveCredentialHelpText(entry.ProviderId, field))).ToArray();
    }

    public static IReadOnlyList<ProviderEnvironmentOptionDto> BuildEnvironmentOptions(ProviderCredentialCatalogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var environments = ResolveAllowedEnvironments(entry);
        if (environments.Count == 0) return [];
        var defaultEnvironment = entry.NormalizeEnvironment(entry.DefaultEnvironment);
        return environments.Select(environment =>
        {
            var normalized = entry.NormalizeEnvironment(environment);
            return new ProviderEnvironmentOptionDto(normalized, LabelEnvironment(normalized), string.Equals(normalized, defaultEnvironment, StringComparison.OrdinalIgnoreCase), ResolveEnvironmentHelpText(entry.ProviderId, normalized));
        }).GroupBy(option => option.Value, StringComparer.OrdinalIgnoreCase).Select(group => group.First()).ToArray();
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
        => fieldName.Contains("Url", StringComparison.OrdinalIgnoreCase) || fieldName.Contains("Endpoint", StringComparison.OrdinalIgnoreCase)
            ? ProviderCredentialInputKindDto.Url
            : fieldName.Equals("RealmId", StringComparison.OrdinalIgnoreCase) || fieldName.Equals("CompanyName", StringComparison.OrdinalIgnoreCase)
                ? ProviderCredentialInputKindDto.Text
                : ProviderCredentialInputKindDto.Password;

    private static string LabelCredentialField(string fieldName)
        => fieldName switch
        {
            "ApiKey" => "API key", "KeyId" => "Key ID", "SecretKey" => "Secret key", "ClientId" => "Client ID", "ClientSecret" => "Client secret", "RefreshToken" => "Refresh token", "RealmId" => "Company realm ID", "CompanyName" => "Company name", "Secret" => "Secret", _ => SplitPascalCase(fieldName)
        };

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
        => value switch { "paper" => "Paper", "live" => "Live", "sandbox" => "Sandbox", "development" => "Development", "production" => "Production", _ => SplitPascalCase(value) };

    private static string ResolveEnvironmentHelpText(string providerId, string environment)
        => ProviderCredentialCatalog.NormalizeProviderId(providerId) switch
        {
            "alpaca" when environment.Equals(AlpacaCredentialEnvironment.LiveEnvironment, StringComparison.OrdinalIgnoreCase) => "Live Alpaca credentials remain gated by live-routing acknowledgement and execution controls.",
            "alpaca" => "Paper is the default Alpaca environment for setup and verification.",
            "plaid" when environment.Equals("production", StringComparison.OrdinalIgnoreCase) => "Production Plaid credentials are used only for server-owned link and evidence sync flows.",
            "plaid" => "Plaid sandbox and development environments support institution linking and evidence tests.",
            "quickbooks" when environment.Equals("production", StringComparison.OrdinalIgnoreCase) => "Production QuickBooks Online access remains read-only GL evidence import.",
            "quickbooks" => "QuickBooks sandbox is the default for read-only GL evidence setup.",
            _ => "Provider environment selected for server-side credential verification."
        };

    private static string SplitPascalCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Credential";
        var chars = new List<char>(value.Length + 8);
        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            if (index > 0 && char.IsUpper(current) && !char.IsWhiteSpace(value[index - 1])) chars.Add(' ');
            chars.Add(current);
        }
        return new string(chars.ToArray());
    }
}

public sealed record ProviderSetupExperienceCatalogEntry(
    string ProviderId,
    string DisplayName,
    ProviderConnectionCapabilityDto Capability,
    string DisplayGroup,
    string ShortDescription,
    string HelpText,
    IReadOnlyList<string> SetupSteps,
    string? WarningCopy,
    IReadOnlyList<string> AffectedWorkflows,
    IReadOnlyList<string> DocsLinks,
    string RecommendedActionWhenMissing,
    string? ActionHref,
    string RecoveryActionLabel)
{
    public string ResolvedActionHref => ActionHref ?? $"/settings#provider-{ProviderId}-connection";

    public static ProviderSetupExperienceCatalogEntry Default(string providerId)
        => new(providerId, providerId, ProviderConnectionCapabilityDto.Data, "Provider", "Configure provider credentials.", "Add and verify credentials before using this provider for repair.", ["Add credentials.", "Save setup.", "Verify provider readiness."], null, [], [], "Add and verify credentials before using this provider for repair.", null, "Verify provider");
}
