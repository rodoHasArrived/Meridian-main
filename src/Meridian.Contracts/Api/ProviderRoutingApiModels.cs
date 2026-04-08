using System.Text.Json.Serialization;

namespace Meridian.Contracts.Api;

/// <summary>
/// Request to create or update a provider connection.
/// </summary>
public sealed record CreateProviderConnectionRequest(
    string? ConnectionId,
    string ProviderFamilyId,
    string DisplayName,
    string ConnectionType = "DataVendor",
    string ConnectionMode = "ReadOnly",
    bool Enabled = true,
    string? CredentialReference = null,
    string? InstitutionId = null,
    string? ExternalAccountId = null,
    ProviderRouteScopeDto? Scope = null,
    string[]? Tags = null,
    string? Description = null,
    bool ProductionReady = false);

/// <summary>
/// Request to create or update a provider binding.
/// </summary>
public sealed record UpdateProviderBindingRequest(
    string? BindingId,
    string Capability,
    string ConnectionId,
    ProviderRouteScopeDto? Target = null,
    int Priority = 100,
    bool Enabled = true,
    string[]? FailoverConnectionIds = null,
    string? SafetyModeOverride = null,
    string? Notes = null);

/// <summary>
/// Request for a route preview.
/// </summary>
public sealed record RoutePreviewRequest(
    string Capability,
    string? Workspace = null,
    string? FundProfileId = null,
    Guid? EntityId = null,
    Guid? SleeveId = null,
    Guid? VehicleId = null,
    Guid? AccountId = null,
    Guid? SecurityId = null,
    string? Symbol = null,
    string? Market = null,
    string? AssetClass = null,
    bool RequireProductionReady = false);

/// <summary>
/// Request to run certification for a connection.
/// </summary>
public sealed record RunCertificationRequest(string ConnectionId);

/// <summary>
/// Request to activate a preset.
/// </summary>
public sealed record ApplyProviderPresetRequest(string PresetId);

/// <summary>
/// Provider connection DTO exposed to clients.
/// </summary>
public sealed record ProviderConnectionDto(
    string ConnectionId,
    string ProviderFamilyId,
    string DisplayName,
    string ConnectionType,
    string ConnectionMode,
    bool Enabled,
    string? CredentialReference,
    string? InstitutionId,
    string? ExternalAccountId,
    ProviderRouteScopeDto? Scope,
    string[] Tags,
    string? Description,
    bool ProductionReady);

/// <summary>
/// Provider binding DTO exposed to clients.
/// </summary>
public sealed record ProviderBindingDto(
    string BindingId,
    string Capability,
    string ConnectionId,
    ProviderRouteScopeDto? Target,
    int Priority,
    bool Enabled,
    string[] FailoverConnectionIds,
    string? SafetyModeOverride,
    string? Notes);

/// <summary>
/// Policy DTO exposed to clients.
/// </summary>
public sealed record ProviderPolicyDto(
    string PolicyId,
    string Capability,
    string SafetyMode,
    bool RequireExplicitBinding,
    bool RequireProductionReady,
    string[] AllowedFailoverConnectionIds,
    string[] AllowedProviderFamilies,
    string? Description);

/// <summary>
/// Preset DTO exposed to clients.
/// </summary>
public sealed record ProviderPresetDto(
    string PresetId,
    string Name,
    string Description,
    string[] Highlights,
    bool IsBuiltIn,
    bool IsEnabled);

/// <summary>
/// Certification DTO exposed to clients.
/// </summary>
public sealed record ProviderCertificationDto(
    string ConnectionId,
    string Status,
    DateTimeOffset? LastRunAt,
    DateTimeOffset? ExpiresAt,
    bool ProductionReady,
    string[] Checks,
    string[] Notes);

/// <summary>
/// Trust score DTO exposed to clients.
/// </summary>
public sealed record ProviderTrustSnapshotDto(
    string ConnectionId,
    string ProviderFamilyId,
    double Score,
    bool IsHealthy,
    string HealthStatus,
    bool IsProductionReady,
    bool IsCertificationFresh,
    string[] Signals);

/// <summary>
/// Candidate item in a route preview.
/// </summary>
public sealed record RoutePreviewCandidateDto(
    string ConnectionId,
    string ProviderFamilyId,
    bool IsHealthy,
    int ScopeRank,
    int Priority,
    string[] ReasonCodes,
    string[] FallbackConnectionIds,
    string? PolicyGate = null);

/// <summary>
/// Route preview response.
/// </summary>
public sealed record RoutePreviewResponse(
    string Capability,
    bool IsRoutable,
    string? SelectedConnectionId,
    string? SelectedProviderFamilyId,
    string SafetyMode,
    bool RequiresManualApproval,
    string[] ReasonCodes,
    string[] SkippedCandidates,
    string[] FallbackConnectionIds,
    string? PolicyGate,
    RoutePreviewCandidateDto[] Candidates);

/// <summary>
/// Shared scope DTO used by provider routing APIs.
/// </summary>
public sealed class ProviderRouteScopeDto
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
