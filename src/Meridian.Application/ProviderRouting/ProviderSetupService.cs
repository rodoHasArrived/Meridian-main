using Meridian.Core.Config;
using Meridian.Application.Config.Credentials;
using Meridian.DataIntegration.Credentials;
using Meridian.Contracts.Api;
using Meridian.Contracts.Configuration;
using Meridian.ProviderSdk;

namespace Meridian.Application.ProviderRouting;

/// <summary>
/// Configures provider setup requests without persisting raw provider credentials in application config.
/// </summary>
public sealed class ProviderSetupService
{
    private const string SetupActor = "provider-setup-service";

    private readonly UI.ConfigStore _store;
    private readonly IProviderCredentialStore _credentialStore;

    public ProviderSetupService(UI.ConfigStore store, IProviderCredentialStore credentialStore)
    {
        _store = store;
        _credentialStore = credentialStore;
    }

    public async Task<ProviderSetupResult> ConfigureAsync(ProviderSetupRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var displayName = request.DisplayName?.Trim();
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return Failure(string.Empty, "Provider display name is required.");
        }

        if (!TryResolveProviderKind(request.Kind, out var sourceKind, out var providerFamilyId, out var credentialOnly, out var providerError))
        {
            return Failure(displayName, providerError);
        }

        var descriptor = ProviderCredentialCatalog.Find(providerFamilyId);
        if (descriptor is null)
        {
            return Failure(displayName, $"Provider '{request.Kind}' is not in the credential catalog.");
        }

        var normalizedCapabilities = NormalizeCapabilities(request.Capabilities);
        var sourceType = ResolveSourceType(normalizedCapabilities);
        var environment = descriptor.NormalizeEnvironment(request.Environment);
        var referenceEnvironment = string.IsNullOrWhiteSpace(environment) ? "default" : environment;
        var credentialReference = descriptor.RequiresCredentials
            ? $"vault:{descriptor.ProviderId}/{referenceEnvironment}"
            : null;
        var warnings = new List<string>();

        var submittedCredentials = BuildCredentialMap(descriptor, request);
        ProviderCredentialStoreStatus credentialStatus;
        if (submittedCredentials.Count > 0)
        {
            if (!descriptor.RequiresCredentials)
            {
                warnings.Add("Submitted credentials were ignored because this provider does not require Meridian-managed credentials.");
            }
            else
            {
                await _credentialStore.SaveAsync(
                    new ProviderCredentialSaveRequest(
                        descriptor.ProviderId,
                        submittedCredentials,
                        string.IsNullOrWhiteSpace(environment) ? null : environment,
                        Actor: SetupActor,
                        Metadata: new Dictionary<string, string>
                        {
                            ["setupSource"] = "legacy-provider-configure"
                        }),
                    ct).ConfigureAwait(false);
            }
        }
        else if (descriptor.RequiresCredentials)
        {
            warnings.Add("No credentials were submitted; configure and verify credentials before relying on this provider for production workflows.");
        }

        credentialStatus = await _credentialStore.GetStatusAsync(descriptor.ProviderId, ct).ConfigureAwait(false);
        if (descriptor.RequiresCredentials && credentialStatus.CredentialState is ProviderCredentialStateDto.Missing or ProviderCredentialStateDto.Partial)
        {
            warnings.Add("Credential setup is incomplete; required credential fields are missing.");
        }

        if (descriptor.ProviderId.Equals("alpaca", StringComparison.OrdinalIgnoreCase) &&
            credentialStatus.Environment?.Equals("live", StringComparison.OrdinalIgnoreCase) == true)
        {
            warnings.Add("Alpaca is configured for a live environment; keep live trading and production routing gated until verification and certification pass.");
        }

        if (credentialOnly)
        {
            return new ProviderSetupResult(
                Success: true,
                ProviderId: descriptor.ProviderId,
                ProviderName: displayName,
                Message: $"{displayName} credentials were configured.",
                Error: null,
                ConnectionId: null,
                BindingIds: Array.Empty<string>(),
                CredentialState: credentialStatus.CredentialState,
                CredentialSource: credentialStatus.CredentialSource,
                CredentialReference: credentialReference,
                Environment: string.IsNullOrWhiteSpace(credentialStatus.Environment) ? null : credentialStatus.Environment,
                Warnings: warnings.ToArray());
        }

        if (sourceKind is null)
        {
            return Failure(displayName, $"Provider '{request.Kind}' is not yet supported by the local data-source configuration model.");
        }

        var cfg = _store.Load();
        var dataSources = cfg.DataSources ?? new DataSourcesConfig();
        var sources = (dataSources.Sources ?? Array.Empty<DataSourceConfig>()).ToList();
        var section = ProviderRoutingConfigExtensions.GetSection(cfg);
        var connections = (section.Connections ?? Array.Empty<ProviderConnectionConfig>()).ToList();
        var bindings = (section.Bindings ?? Array.Empty<ProviderBindingConfig>()).ToList();
        var providerId = BuildProviderId(descriptor.ProviderId, sources, connections);
        var sourcePriority = sources.Count == 0 ? 10 : Math.Max(10, sources.Count * 10 + 10);

        sources.Add(new DataSourceConfig(
            Id: providerId,
            Name: displayName,
            Provider: sourceKind.Value,
            Enabled: true,
            Type: sourceType,
            Priority: sourcePriority,
            Alpaca: sourceKind.Value == DataSourceKind.Alpaca
                ? new AlpacaOptions(UseSandbox: IsSandboxEnvironment(credentialStatus.Environment))
                : null,
            Polygon: sourceKind.Value == DataSourceKind.Polygon
                ? new PolygonOptions()
                : null,
            IB: sourceKind.Value == DataSourceKind.IB
                ? BuildInteractiveBrokersOptions(request.Endpoint)
                : null,
            Description: $"Configured from the provider setup form for {displayName}.",
            Tags: normalizedCapabilities));

        var connection = new ProviderConnectionConfig(
            ConnectionId: providerId,
            ProviderFamilyId: descriptor.ProviderId,
            DisplayName: displayName,
            ConnectionType: ProviderConnectionType.DataVendor,
            ConnectionMode: ResolveConnectionMode(credentialStatus.Environment, normalizedCapabilities),
            Enabled: true,
            CredentialReference: credentialReference,
            Tags: normalizedCapabilities,
            Description: $"Configured from the provider setup form for {displayName}.",
            ProductionReady: false);

        var existingConnectionIndex = connections.FindIndex(c =>
            string.Equals(c.ConnectionId, providerId, StringComparison.OrdinalIgnoreCase));
        if (existingConnectionIndex >= 0)
        {
            connections[existingConnectionIndex] = connection;
        }
        else
        {
            connections.Add(connection);
        }

        var seededBindingIds = SeedBindings(providerId, normalizedCapabilities, sourceType, sourcePriority, bindings);
        var nextDataSources = dataSources with
        {
            Sources = sources.ToArray(),
            DefaultRealTimeSourceId = dataSources.DefaultRealTimeSourceId ?? (sourceType is DataSourceType.RealTime or DataSourceType.Both ? providerId : null),
            DefaultHistoricalSourceId = dataSources.DefaultHistoricalSourceId ?? (sourceType is DataSourceType.Historical or DataSourceType.Both ? providerId : null)
        };

        await _store.SaveAsync(cfg with
        {
            DataSources = nextDataSources,
            ProviderConnections = section with
            {
                Connections = connections.ToArray(),
                Bindings = bindings.ToArray()
            }
        }, ct).ConfigureAwait(false);

        return new ProviderSetupResult(
            Success: true,
            ProviderId: providerId,
            ProviderName: displayName,
            Message: $"{displayName} was configured.",
            Error: null,
            ConnectionId: providerId,
            BindingIds: seededBindingIds,
            CredentialState: credentialStatus.CredentialState,
            CredentialSource: credentialStatus.CredentialSource,
            CredentialReference: credentialReference,
            Environment: string.IsNullOrWhiteSpace(credentialStatus.Environment) ? null : credentialStatus.Environment,
            Warnings: warnings.ToArray());
    }

    private static ProviderSetupResult Failure(string providerName, string error)
        => new(
            Success: false,
            ProviderId: null,
            ProviderName: providerName,
            Message: error,
            Error: error,
            Warnings: Array.Empty<string>());

    private static bool TryResolveProviderKind(
        string? kind,
        out DataSourceKind? sourceKind,
        out string providerFamilyId,
        out bool credentialOnly,
        out string error)
    {
        var normalizedKind = NormalizeProviderKind(kind);
        error = string.Empty;
        credentialOnly = false;
        (sourceKind, providerFamilyId, credentialOnly) = normalizedKind switch
        {
            "alpaca" => ((DataSourceKind?)DataSourceKind.Alpaca, "alpaca", false),
            "polygon" => ((DataSourceKind?)DataSourceKind.Polygon, "polygon", false),
            "yahoo" or "yahoofinance" => ((DataSourceKind?)DataSourceKind.Yahoo, "yahoo", false),
            "interactivebrokers" or "ib" => ((DataSourceKind?)DataSourceKind.IB, "ib", false),
            "synthetic" or "custom" => ((DataSourceKind?)DataSourceKind.Synthetic, "synthetic", false),
            "plaid" or "plaidapi" => ((DataSourceKind?)null, "plaid", true),
            _ => ((DataSourceKind?)null, string.Empty, false)
        };

        if (!string.IsNullOrWhiteSpace(providerFamilyId))
        {
            return true;
        }

        error = $"Provider '{kind}' is not yet supported by the local data-source configuration model.";
        return false;
    }

    private static string NormalizeProviderKind(string? kind)
    {
        var normalized = (kind ?? string.Empty).Trim().ToLowerInvariant();
        return normalized.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);
    }

    private static DataSourceType ResolveSourceType(IReadOnlyList<string> capabilities)
    {
        var hasStreaming = capabilities.Any(capability => ResolvesCapability(capability, ProviderCapabilityKind.RealtimeMarketData));
        var hasBackfill = capabilities.Any(capability => ResolvesCapability(capability, ProviderCapabilityKind.HistoricalBars));
        return (hasStreaming, hasBackfill) switch
        {
            (true, true) => DataSourceType.Both,
            (false, true) => DataSourceType.Historical,
            _ => DataSourceType.RealTime
        };
    }

    private static string[] NormalizeCapabilities(IReadOnlyList<string>? capabilities)
        => capabilities?
            .Where(capability => !string.IsNullOrWhiteSpace(capability))
            .Select(CanonicalizeCapability)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(capability => capability, StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? Array.Empty<string>();

    private static Dictionary<string, string?> BuildCredentialMap(
        ProviderCredentialCatalogEntry descriptor,
        ProviderSetupRequest request)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var apiKey = NullIfBlank(request.ApiKey);
        var apiSecret = NullIfBlank(request.ApiSecret);

        if (descriptor.ProviderId.Equals("alpaca", StringComparison.OrdinalIgnoreCase))
        {
            if (apiKey is not null)
            {
                values["KeyId"] = apiKey;
            }

            if (apiSecret is not null)
            {
                values["SecretKey"] = apiSecret;
            }

            return values;
        }

        if (descriptor.RequiredFields.Count > 0 && apiKey is not null)
        {
            values[descriptor.RequiredFields[0].Name] = apiKey;
        }

        if (descriptor.RequiredFields.Count > 1 && apiSecret is not null)
        {
            values[descriptor.RequiredFields[1].Name] = apiSecret;
        }

        return values;
    }

    private static string BuildProviderId(
        string providerFamilyId,
        IReadOnlyList<DataSourceConfig> sources,
        IReadOnlyList<ProviderConnectionConfig> connections)
    {
        var baseId = string.IsNullOrWhiteSpace(providerFamilyId) ? "provider" : providerFamilyId;
        if (!Exists(baseId))
        {
            return baseId;
        }

        var suffix = 2;
        string candidate;
        do
        {
            candidate = $"{baseId}-{suffix++}";
        }
        while (Exists(candidate));

        return candidate;

        bool Exists(string id)
            => sources.Any(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase)) ||
               connections.Any(c => string.Equals(c.ConnectionId, id, StringComparison.OrdinalIgnoreCase));
    }

    private static string[] SeedBindings(
        string providerId,
        IReadOnlyList<string> capabilities,
        DataSourceType sourceType,
        int priority,
        List<ProviderBindingConfig> bindings)
    {
        var capabilityKinds = ResolveCapabilityKinds(capabilities, sourceType);
        var seeded = new List<string>();
        foreach (var capability in capabilityKinds)
        {
            var bindingId = BuildBindingId(providerId, capability, bindings);
            bindings.Add(new ProviderBindingConfig(
                BindingId: bindingId,
                Capability: capability,
                ConnectionId: providerId,
                Target: new ProviderBindingTarget(),
                Priority: priority,
                Enabled: true,
                Notes: "Seeded by provider setup."));
            seeded.Add(bindingId);
        }

        return seeded.ToArray();
    }

    private static IReadOnlyList<ProviderCapabilityKind> ResolveCapabilityKinds(
        IReadOnlyList<string> capabilities,
        DataSourceType sourceType)
    {
        var kinds = new List<ProviderCapabilityKind>();
        foreach (var capability in capabilities)
        {
            var kind = ResolveCapabilityKind(capability);

            if (kind is not null && !kinds.Contains(kind.Value))
            {
                kinds.Add(kind.Value);
            }
        }

        if (kinds.Count > 0)
        {
            return kinds;
        }

        return sourceType switch
        {
            DataSourceType.Historical => [ProviderCapabilityKind.HistoricalBars],
            DataSourceType.Both => [ProviderCapabilityKind.RealtimeMarketData, ProviderCapabilityKind.HistoricalBars],
            _ => [ProviderCapabilityKind.RealtimeMarketData]
        };
    }

    private static bool ResolvesCapability(string capability, ProviderCapabilityKind expected)
        => ResolveCapabilityKind(capability) == expected;

    private static ProviderCapabilityKind? ResolveCapabilityKind(string? capability)
        => NormalizeCapabilityAlias(capability) switch
        {
            "streaming" or "realtime" or "realtimemarketdata" or "realtimedata"
                => ProviderCapabilityKind.RealtimeMarketData,
            "backfill" or "historical" or "historicalbars" or "historicaldata"
                => ProviderCapabilityKind.HistoricalBars,
            "reference" or "referencedata"
                => ProviderCapabilityKind.ReferenceData,
            _ => null
        };

    private static string CanonicalizeCapability(string? capability)
        => ResolveCapabilityKind(capability) switch
        {
            ProviderCapabilityKind.RealtimeMarketData => "realtime-market-data",
            ProviderCapabilityKind.HistoricalBars => "historical-bars",
            ProviderCapabilityKind.ReferenceData => "reference-data",
            _ => (capability ?? string.Empty).Trim().ToLowerInvariant()
        };

    private static string NormalizeCapabilityAlias(string? capability)
    {
        var normalized = (capability ?? string.Empty).Trim().ToLowerInvariant();
        return normalized.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);
    }

    private static string BuildBindingId(
        string providerId,
        ProviderCapabilityKind capability,
        IReadOnlyList<ProviderBindingConfig> bindings)
    {
        var baseId = $"{providerId}-{ToKebabCase(capability.ToString())}";
        if (!Exists(baseId))
        {
            return baseId;
        }

        var suffix = 2;
        string candidate;
        do
        {
            candidate = $"{baseId}-{suffix++}";
        }
        while (Exists(candidate));

        return candidate;

        bool Exists(string id)
            => bindings.Any(binding => string.Equals(binding.BindingId, id, StringComparison.OrdinalIgnoreCase));
    }

    private static ProviderConnectionMode ResolveConnectionMode(
        string? environment,
        IReadOnlyList<string> capabilities)
    {
        if (environment is not null)
        {
            if (environment.Equals("live", StringComparison.OrdinalIgnoreCase) ||
                environment.Equals("production", StringComparison.OrdinalIgnoreCase))
            {
                return ProviderConnectionMode.Live;
            }

            if (environment.Equals("paper", StringComparison.OrdinalIgnoreCase) ||
                environment.Equals("sandbox", StringComparison.OrdinalIgnoreCase))
            {
                return ProviderConnectionMode.Paper;
            }
        }

        return capabilities.Any(capability => ResolvesCapability(capability, ProviderCapabilityKind.RealtimeMarketData)) ||
               capabilities.Any(capability => ResolvesCapability(capability, ProviderCapabilityKind.HistoricalBars)) ||
               capabilities.Any(capability => ResolvesCapability(capability, ProviderCapabilityKind.ReferenceData))
            ? ProviderConnectionMode.ReadOnly
            : ProviderConnectionMode.Research;
    }

    private static IBOptions BuildInteractiveBrokersOptions(string? endpoint)
    {
        var host = "127.0.0.1";
        var port = 7497;
        if (Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
        {
            host = uri.Host;
            port = uri.Port > 0 ? uri.Port : port;
        }
        else if (!string.IsNullOrWhiteSpace(endpoint))
        {
            var parts = endpoint.Split(':', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            host = parts[0];
            if (parts.Length == 2 && int.TryParse(parts[1], out var parsedPort))
            {
                port = parsedPort;
            }
        }

        return new IBOptions(Host: host, Port: port);
    }

    private static bool IsSandboxEnvironment(string? environment)
        => environment is not null &&
           (environment.Equals("sandbox", StringComparison.OrdinalIgnoreCase) ||
            environment.Equals("paper", StringComparison.OrdinalIgnoreCase));

    private static string ToKebabCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "capability";
        }

        var chars = new List<char>(value.Length + 8);
        for (var i = 0; i < value.Length; i++)
        {
            var current = value[i];
            if (char.IsUpper(current) && i > 0)
            {
                chars.Add('-');
            }

            chars.Add(char.ToLowerInvariant(current));
        }

        return new string(chars.ToArray());
    }

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
