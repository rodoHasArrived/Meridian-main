using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Meridian.Contracts.Api;
using Meridian.Contracts.Integrity;
using Meridian.Core.Config;
using Meridian.DataIntegration.Credentials;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.ProviderSdk;

namespace Meridian.Application.ProviderRouting;

internal static partial class ProviderConnectionTemplateCatalog
{
    internal const int SchemaVersion = 1;
    internal const int MaxFileLength = 64 * 1024;
    internal const int MaxTemplates = 100;
    private const int ProviderVersion = 1;
    private const int CapabilityVersion = 1;

    internal static readonly JsonSerializerOptions FileJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        AllowDuplicateProperties = false,
        NumberHandling = JsonNumberHandling.Strict,
        MaxDepth = 12
    };

    internal static IReadOnlyList<ProviderConnectionTemplateDto> BuiltIns { get; } =
    [
        BuiltIn("Paper brokerage", "alpaca", "paper", ProviderConnectionType.Brokerage, ProviderConnectionMode.Paper,
            "Tenant", "PaperAccount", 100, ProviderCapabilityKind.AccountBalances, ProviderCapabilityKind.OrderExecution),
        BuiltIn("Historical equities", "tiingo", "production", ProviderConnectionType.DataVendor, ProviderConnectionMode.ReadOnly,
            "Tenant", "EquityDailyBars", 100, ProviderCapabilityKind.HistoricalBars),
        BuiltIn("Options chain", "polygon", "production", ProviderConnectionType.DataVendor, ProviderConnectionMode.ReadOnly,
            "Tenant", "OptionChain", 100, ProviderCapabilityKind.OptionsChain),
        BuiltIn("Reference data", "openfigi", "production", ProviderConnectionType.DataVendor, ProviderConnectionMode.ReadOnly,
            "Tenant", "IdentifierMapping", 100, ProviderCapabilityKind.ReferenceData),
        BuiltIn("Research-only backfill", "stooq", "public", ProviderConnectionType.DataVendor, ProviderConnectionMode.Research,
            "Research", "ResearchBars", 200, ProviderCapabilityKind.HistoricalBars)
    ];

    private static readonly Dictionary<string, string> Semantics = new(StringComparer.Ordinal)
    {
        ["PaperAccount"] = "Simulated brokerage account state and paper order execution.",
        ["EquityDailyBars"] = "Daily equity OHLCV. Confirm exchange calendars and adjusted versus raw prices.",
        ["OptionChain"] = "Point-in-time option contracts and quotes. Confirm feed delay, coverage, and entitlements.",
        ["IdentifierMapping"] = "Identifier and symbol mappings. Check exchange, currency, and ambiguous matches.",
        ["ResearchBars"] = "Historical bars for research. Check coverage, adjustments, gaps, and permitted use.",
        ["ProviderContract"] = "Data follows the provider contract for the requested capabilities."
    };

    private static readonly Dictionary<string, string> Checks = new(StringComparer.Ordinal)
    {
        ["AccountEnvironment"] = "Verify the account identity and environment against the provider.",
        ["CapabilitySample"] = "Run a representative sample for each requested capability and check access and coverage.",
        ["EquityBarBasis"] = "Compare sample OHLCV, price adjustments, and session dates with a known source.",
        ["OptionContractKeys"] = "Check underlying, expiry, strike, put/call, quote time, and feed entitlement.",
        ["ReferenceIdentity"] = "Check a known identifier, security type, exchange, and an ambiguous or missing match.",
        ["ResearchCoverage"] = "Check date coverage, gaps, adjustments, and research-use licensing."
    };

    internal static ProviderConnectionTemplateOption Describe(ProviderConnectionTemplateDto template, bool builtIn)
    {
        var provider = ProviderCredentialCatalog.Find(template.ProviderKind)!;
        return new(template, Revision(template), builtIn, RequiresAccount(template),
        [
            new("Display name", template.Name),
            new("Provider kind", provider.DisplayName),
            new("Environment", template.Environment),
            new("Connection type", template.ConnectionType),
            new("Operating mode", template.ConnectionMode),
            new("Allowed scope", template.AllowedScope == "Tenant" ? "Current tenant" : $"{template.AllowedScope} workspace"),
            new("Required capabilities", string.Join(", ", template.RequestedCapabilities.Select(c => CapabilityLabel(c.Capability)))),
            new("Suggested binding", $"{CapabilityLabel(template.SuggestedBinding.Capability)} (priority {template.SuggestedBinding.Priority})"),
            new("Expected data semantics", Semantics[template.ExpectedDataSemantics])
        ],
            template.VerificationChecklist.Select(check => Checks[check]).ToArray());
    }

    internal static string Revision(ProviderConnectionTemplateDto template)
        => Sha256Digest.Compute(JsonSerializer.SerializeToUtf8Bytes(template, FileJson));

    internal static bool RequiresAccount(ProviderConnectionTemplateDto template)
        => template.ProviderKind == "alpaca" || template.ConnectionType is "Brokerage" or "Bank" or "Custodian"
            || ProviderCredentialCatalog.Find(template.ProviderKind)?.Capability == Contracts.Configuration.ProviderConnectionCapabilityDto.AccountingSystem;

    internal static bool IsWorkspaceScope(string? scope) => scope is "Data" or "Trading" or "Research";

    internal static string[] KnownEnvironments(ProviderCredentialCatalogEntry provider)
    {
        var options = ProviderCredentialCatalog.BuildEnvironmentOptions(provider);
        if (options.Count > 0)
            return options.Select(option => option.Value).ToArray();
        return provider.RequiresCredentials ? ["production"] : ["public"];
    }

    internal static IReadOnlyList<ProviderTemplateImportIssue> Validate(ProviderConnectionTemplateDto? template, string field)
    {
        if (template is null)
            return [new(field, "InvalidTemplate", "A template object is required.")];

        var issues = new List<ProviderTemplateImportIssue>();
        void Issue(string property, string code, string message) => issues.Add(new(field + "." + property, code, message));

        if (template.Name is not { Length: > 0 and <= 64 } || !PortableName().IsMatch(template.Name))
            Issue("name", "InvalidName", "Use a short name made of words, with spaces or hyphens between them.");
        var provider = template.ProviderKind is { Length: > 0 }
            ? ProviderCredentialCatalog.Find(template.ProviderKind) : null;
        if (provider is null || provider.ProviderId != template.ProviderKind)
            Issue("providerKind", "UnknownProvider", "This provider kind is unknown or has no managed vault contract.");
        else if (!KnownEnvironments(provider).Contains(template.Environment, StringComparer.Ordinal))
            Issue("environment", "UnsupportedEnvironment", "This provider does not expose the requested environment.");
        if (template.ProviderVersion != ProviderVersion)
            Issue("providerVersion", "UnsupportedProviderVersion", "This host supports provider contract version 1.");

        if (!TryNamedEnum<ProviderConnectionType>(template.ConnectionType, out var type))
            Issue("connectionType", "UnsupportedConnectionType", "Use a supported connection type.");
        if (!TryNamedEnum<ProviderConnectionMode>(template.ConnectionMode, out var mode) ||
            mode == ProviderConnectionMode.Paper && template.Environment is not ("paper" or "sandbox") ||
            mode == ProviderConnectionMode.Live && template.Environment is not ("live" or "production"))
            Issue("connectionMode", "UnsupportedConnectionMode", "The operating mode must match the provider environment.");
        if (template.AllowedScope != "Tenant" && !IsWorkspaceScope(template.AllowedScope) ||
            mode == ProviderConnectionMode.Research && template.AllowedScope != "Research")
            Issue("allowedScope", "UnsupportedScope", "Use a portable tenant or workspace scope. Research mode requires the Research workspace.");

        var capabilityNames = new HashSet<string>(StringComparer.Ordinal);
        if (template.RequestedCapabilities is not { Length: > 0 })
            Issue("requestedCapabilities", "MissingCapabilities", "At least one required capability must be declared.");
        else
            for (var i = 0; i < template.RequestedCapabilities.Length; i++)
            {
                var item = template.RequestedCapabilities[i];
                var path = $"requestedCapabilities[{i}]";
                if (item is null)
                {
                    Issue(path, "InvalidCapability", "A capability and version are required.");
                    continue;
                }

                if (!TryNamedEnum<ProviderCapabilityKind>(item.Capability, out var capability))
                    Issue(path + ".capability", "UnknownCapability", "This capability kind is unknown.");
                else if (!capabilityNames.Add(item.Capability))
                    Issue(path + ".capability", "DuplicateCapability", "Declare each required capability once.");
                else if (provider is not null && (!Supports(provider, capability, mode) ||
                    capability == ProviderCapabilityKind.OrderExecution && type != ProviderConnectionType.Brokerage))
                    Issue(path + ".capability", "UnsupportedCapability", "The provider or operating mode does not support this capability.");
                if (item.Version != CapabilityVersion)
                    Issue(path + ".version", "UnsupportedCapabilityVersion", "This host supports capability contract version 1.");
            }

        if (template.SuggestedBinding is not { Priority: >= 0 and <= 999 } binding || !capabilityNames.Contains(binding.Capability))
            Issue("suggestedBinding", "InvalidBinding", "Suggest a required capability and a priority between 0 and 999.");
        if (template.ExpectedDataSemantics is null || !Semantics.ContainsKey(template.ExpectedDataSemantics))
            Issue("expectedDataSemantics", "UnknownSemantics", "Use a data semantics code supported by this host.");
        if (template.VerificationChecklist is not { Length: > 0 } checklist ||
            checklist.Any(check => check is null || !Checks.ContainsKey(check)) || checklist.Distinct().Count() != checklist.Length)
            Issue("verificationChecklist", "InvalidChecklist", "Use unique verification checklist codes supported by this host.");
        return issues;
    }

    internal static ProviderConnectionTemplateDto FromConnection(ProviderConnectionConfig source, string name,
        IReadOnlyList<ProviderBindingConfig> bindings)
    {
        var provider = ProviderCredentialCatalog.Find(source.ProviderFamilyId)
            ?? throw new InvalidOperationException("This provider has no managed vault contract for template setup.");
        var scope = source.Scope;
        if (scope is not null && (scope.FundProfileId is not null || scope.EntityId is not null || scope.SleeveId is not null ||
            scope.VehicleId is not null || scope.AccountId is not null))
            throw new InvalidOperationException("This connection uses a private fund, entity, or account scope and cannot be copied as a portable template.");
        var allowedScope = scope?.Workspace?.Trim().ToLowerInvariant() switch
        {
            null or "" when source.ConnectionMode == ProviderConnectionMode.Research => "Research",
            null or "" => "Tenant",
            "data" => "Data",
            "trading" => "Trading",
            "research" => "Research",
            _ => throw new InvalidOperationException("This connection uses a private workspace scope and cannot be copied as a portable template.")
        };
        var supported = bindings.Where(binding => binding.ConnectionId == source.ConnectionId &&
                Supports(provider, binding.Capability, source.ConnectionMode))
            .OrderBy(binding => binding.Priority).ThenBy(binding => binding.Capability).ToArray();
        var suggestion = supported.FirstOrDefault();
        var retained = source.SetupTemplate;
        var capabilities = retained?.RequestedCapabilities ?? supported.Select(binding => binding.Capability).Distinct().Select(Capability).ToArray();
        if (capabilities.Length == 0)
        {
            // An older connection can have no retained capability selection or bindings.
            var defaults = new[] { ProviderCapabilityKind.HistoricalBars, ProviderCapabilityKind.ReferenceData,
                ProviderCapabilityKind.AccountBalances }.Concat(Enum.GetValues<ProviderCapabilityKind>());
            var capability = defaults.Where(candidate => Supports(provider, candidate, source.ConnectionMode))
                .Select(candidate => (ProviderCapabilityKind?)candidate).FirstOrDefault();
            if (capability is null)
                throw new InvalidOperationException("The connection has no supported portable capability.");
            capabilities = [Capability(capability.Value)];
        }

        var binding = suggestion is not null && capabilities.Any(capability => capability.Capability == suggestion.Capability.ToString())
            ? new ProviderTemplateBindingDto(suggestion.Capability.ToString(), suggestion.Priority)
            : retained?.SuggestedBinding ?? new ProviderTemplateBindingDto(capabilities[0].Capability, 100);
        var semantics = retained ?? BuiltIns.FirstOrDefault(template => template.ProviderKind == provider.ProviderId &&
            template.ConnectionMode == source.ConnectionMode.ToString() && template.SuggestedBinding.Capability == binding.Capability);
        var environment = source.CredentialEnvironment ?? KnownEnvironments(provider)[0];
        return new(name.Trim(), provider.ProviderId, ProviderVersion, environment, source.ConnectionType.ToString(),
            source.ConnectionMode.ToString(), allowedScope, capabilities, binding, semantics?.ExpectedDataSemantics ?? "ProviderContract",
            semantics?.VerificationChecklist ?? ["CapabilitySample"]);
    }

    private static bool Supports(ProviderCredentialCatalogEntry provider, ProviderCapabilityKind kind, ProviderConnectionMode mode)
    {
        var adapter = ProviderCapabilityDescriptorCatalog.Descriptors.FirstOrDefault(item => item.ProviderId == provider.ProviderId);
        var brokerage = provider.Capability is Contracts.Configuration.ProviderConnectionCapabilityDto.Brokerage
            or Contracts.Configuration.ProviderConnectionCapabilityDto.DataAndBrokerage;
        var accounts = brokerage || provider.Capability == Contracts.Configuration.ProviderConnectionCapabilityDto.AccountingSystem ||
            provider.ProviderId == "plaid";
        return kind switch
        {
            ProviderCapabilityKind.RealtimeMarketData => adapter?.HasStreaming == true && mode != ProviderConnectionMode.Research,
            ProviderCapabilityKind.HistoricalBars => adapter?.HasHistorical == true,
            ProviderCapabilityKind.OptionsChain => adapter?.HasOptions == true,
            ProviderCapabilityKind.ReferenceData or ProviderCapabilityKind.SecurityMasterSeed
                => adapter is { HasSearch: true } or { HasSymbolResolver: true },
            ProviderCapabilityKind.SymbolSearch => adapter?.HasSearch == true,
            ProviderCapabilityKind.CorporateActions => adapter?.HasCorporateActions == true,
            ProviderCapabilityKind.OrderExecution => brokerage && adapter?.HasBrokerage == true &&
                mode is ProviderConnectionMode.Paper or ProviderConnectionMode.Live,
            ProviderCapabilityKind.AccountBalances or ProviderCapabilityKind.AccountPositions or ProviderCapabilityKind.ExecutionHistory
                => accounts && mode != ProviderConnectionMode.Research,
            ProviderCapabilityKind.CashTransactions or ProviderCapabilityKind.BankStatements
                => provider.ProviderId == "plaid" && mode != ProviderConnectionMode.Research,
            ProviderCapabilityKind.ReconciliationFeed => accounts && mode != ProviderConnectionMode.Research,
            _ => false
        };
    }

    private static ProviderConnectionTemplateDto BuiltIn(string name, string provider, string environment, ProviderConnectionType type,
        ProviderConnectionMode mode, string scope, string semantics, int priority, params ProviderCapabilityKind[] capabilities)
    {
        var check = semantics switch
        {
            "PaperAccount" => "AccountEnvironment",
            "EquityDailyBars" => "EquityBarBasis",
            "OptionChain" => "OptionContractKeys",
            "IdentifierMapping" => "ReferenceIdentity",
            _ => "ResearchCoverage"
        };
        return new(name, provider, ProviderVersion, environment, type.ToString(), mode.ToString(), scope,
            capabilities.Select(Capability).ToArray(), new(capabilities[0].ToString(), priority), semantics, [check, "CapabilitySample"]);
    }

    private static ProviderTemplateCapabilityDto Capability(ProviderCapabilityKind kind) => new(kind.ToString(), CapabilityVersion);

    private static bool TryNamedEnum<T>(string? value, out T parsed) where T : struct, Enum
        => Enum.TryParse(value, out parsed) && parsed.ToString() == value && Enum.IsDefined(parsed);

    private static string CapabilityLabel(string capability) => CapabilityWordBoundary().Replace(capability, " ");

    [GeneratedRegex(@"\A[A-Za-z]{1,24}(?:[ -][A-Za-z]{1,24})*\z")]
    private static partial Regex PortableName();

    [GeneratedRegex("(?<=[a-z])(?=[A-Z])")]
    private static partial Regex CapabilityWordBoundary();
}
