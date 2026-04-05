using Meridian.ProviderSdk;

namespace Meridian.Application.Config;

/// <summary>
/// Relationship-aware provider operations configuration.
/// </summary>
public sealed record ProviderConnectionsConfig(
    ProviderConnectionConfig[]? Connections = null,
    ProviderBindingConfig[]? Bindings = null,
    ProviderPolicyConfig[]? Policies = null,
    ProviderPresetConfig[]? Presets = null,
    ProviderCertificationConfig[]? Certifications = null
);

/// <summary>
/// One credentialed provider relationship.
/// </summary>
public sealed record ProviderConnectionConfig(
    string ConnectionId,
    string ProviderFamilyId,
    string DisplayName,
    ProviderConnectionType ConnectionType = ProviderConnectionType.DataVendor,
    ProviderConnectionMode ConnectionMode = ProviderConnectionMode.ReadOnly,
    bool Enabled = true,
    string? CredentialReference = null,
    string? InstitutionId = null,
    string? ExternalAccountId = null,
    ProviderConnectionScope? Scope = null,
    string[]? Tags = null,
    string? Description = null,
    bool ProductionReady = false
);

/// <summary>
/// Capability binding targeting one connection and one scope.
/// </summary>
public sealed record ProviderBindingConfig(
    string BindingId,
    ProviderCapabilityKind Capability,
    string ConnectionId,
    ProviderBindingTarget? Target = null,
    int Priority = 100,
    bool Enabled = true,
    string[]? FailoverConnectionIds = null,
    ProviderSafetyMode? SafetyModeOverride = null,
    string? Notes = null
);

/// <summary>
/// Policy override for a capability.
/// </summary>
public sealed record ProviderPolicyConfig(
    string PolicyId,
    ProviderCapabilityKind Capability,
    ProviderSafetyMode SafetyMode = ProviderSafetyMode.HealthAwareFailover,
    bool RequireExplicitBinding = false,
    bool RequireProductionReady = false,
    string[]? AllowedFailoverConnectionIds = null,
    string[]? AllowedProviderFamilies = null,
    string? Description = null
);

/// <summary>
/// Reusable provider operations preset.
/// </summary>
public sealed record ProviderPresetConfig(
    string PresetId,
    string Name,
    string Description,
    ProviderPolicyConfig[]? Policies = null,
    string[]? Highlights = null,
    bool IsBuiltIn = false,
    bool IsEnabled = false
);

/// <summary>
/// Stored certification state for a provider connection.
/// </summary>
public sealed record ProviderCertificationConfig(
    string ConnectionId,
    string Status = "Pending",
    DateTimeOffset? LastRunAt = null,
    DateTimeOffset? ExpiresAt = null,
    bool ProductionReady = false,
    string[]? Checks = null,
    string[]? Notes = null
);
