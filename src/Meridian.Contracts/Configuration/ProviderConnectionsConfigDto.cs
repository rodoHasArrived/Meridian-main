using System.Text.Json.Serialization;

namespace Meridian.Contracts.Configuration;

/// <summary>
/// DTO version of relationship-aware provider operations configuration.
/// </summary>
public sealed class ProviderConnectionsConfigDto
{
    [JsonPropertyName("connections")]
    public ProviderConnectionConfigDto[]? Connections { get; set; }

    [JsonPropertyName("bindings")]
    public ProviderBindingConfigDto[]? Bindings { get; set; }

    [JsonPropertyName("policies")]
    public ProviderPolicyConfigDto[]? Policies { get; set; }

    [JsonPropertyName("presets")]
    public ProviderPresetConfigDto[]? Presets { get; set; }

    [JsonPropertyName("certifications")]
    public ProviderCertificationConfigDto[]? Certifications { get; set; }
}

public sealed class ProviderConnectionConfigDto
{
    [JsonPropertyName("connectionId")]
    public string ConnectionId { get; set; } = string.Empty;

    [JsonPropertyName("providerFamilyId")]
    public string ProviderFamilyId { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("connectionType")]
    public string ConnectionType { get; set; } = "DataVendor";

    [JsonPropertyName("connectionMode")]
    public string ConnectionMode { get; set; } = "ReadOnly";

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("credentialReference")]
    public string? CredentialReference { get; set; }

    [JsonPropertyName("institutionId")]
    public string? InstitutionId { get; set; }

    [JsonPropertyName("externalAccountId")]
    public string? ExternalAccountId { get; set; }

    [JsonPropertyName("scope")]
    public ProviderScopeDto? Scope { get; set; }

    [JsonPropertyName("tags")]
    public string[]? Tags { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("productionReady")]
    public bool ProductionReady { get; set; }
}

public sealed class ProviderBindingConfigDto
{
    [JsonPropertyName("bindingId")]
    public string BindingId { get; set; } = string.Empty;

    [JsonPropertyName("capability")]
    public string Capability { get; set; } = "RealtimeMarketData";

    [JsonPropertyName("connectionId")]
    public string ConnectionId { get; set; } = string.Empty;

    [JsonPropertyName("target")]
    public ProviderScopeDto? Target { get; set; }

    [JsonPropertyName("priority")]
    public int Priority { get; set; } = 100;

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("failoverConnectionIds")]
    public string[]? FailoverConnectionIds { get; set; }

    [JsonPropertyName("safetyModeOverride")]
    public string? SafetyModeOverride { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }
}

public sealed class ProviderPolicyConfigDto
{
    [JsonPropertyName("policyId")]
    public string PolicyId { get; set; } = string.Empty;

    [JsonPropertyName("capability")]
    public string Capability { get; set; } = "RealtimeMarketData";

    [JsonPropertyName("safetyMode")]
    public string SafetyMode { get; set; } = "HealthAwareFailover";

    [JsonPropertyName("requireExplicitBinding")]
    public bool RequireExplicitBinding { get; set; }

    [JsonPropertyName("requireProductionReady")]
    public bool RequireProductionReady { get; set; }

    [JsonPropertyName("allowedFailoverConnectionIds")]
    public string[]? AllowedFailoverConnectionIds { get; set; }

    [JsonPropertyName("allowedProviderFamilies")]
    public string[]? AllowedProviderFamilies { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

public sealed class ProviderPresetConfigDto
{
    [JsonPropertyName("presetId")]
    public string PresetId { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("policies")]
    public ProviderPolicyConfigDto[]? Policies { get; set; }

    [JsonPropertyName("highlights")]
    public string[]? Highlights { get; set; }

    [JsonPropertyName("isBuiltIn")]
    public bool IsBuiltIn { get; set; }

    [JsonPropertyName("isEnabled")]
    public bool IsEnabled { get; set; }
}

public sealed class ProviderCertificationConfigDto
{
    [JsonPropertyName("connectionId")]
    public string ConnectionId { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = "Pending";

    [JsonPropertyName("lastRunAt")]
    public DateTimeOffset? LastRunAt { get; set; }

    [JsonPropertyName("expiresAt")]
    public DateTimeOffset? ExpiresAt { get; set; }

    [JsonPropertyName("productionReady")]
    public bool ProductionReady { get; set; }

    [JsonPropertyName("checks")]
    public string[]? Checks { get; set; }

    [JsonPropertyName("notes")]
    public string[]? Notes { get; set; }
}

public sealed class ProviderScopeDto
{
    [JsonPropertyName("workspace")]
    public string? Workspace { get; set; }

    [JsonPropertyName("fundProfileId")]
    public string? FundProfileId { get; set; }

    [JsonPropertyName("entityId")]
    public Guid? EntityId { get; set; }

    [JsonPropertyName("sleeveId")]
    public Guid? SleeveId { get; set; }

    [JsonPropertyName("vehicleId")]
    public Guid? VehicleId { get; set; }

    [JsonPropertyName("accountId")]
    public Guid? AccountId { get; set; }
}
