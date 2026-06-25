namespace Meridian.Ui.Services.Services.ProviderSetup;

/// <summary>
/// Status of a configured provider module — returned by the module setup API.
/// Credential values are NEVER included; only key names + hasValue flag.
/// </summary>
public sealed record ProviderModuleStatusDto(
    string ModuleId,
    string DisplayName,
    bool Enabled,
    int Priority,
    bool IsConfigured,
    bool IsRunning,
    IReadOnlyList<CredentialKeyStatusDto> CredentialStatus,
    IReadOnlyList<string> CapabilityNames,
    string? LastTestResult = null,
    DateTimeOffset? LastTestedAt = null);

/// <summary>
/// Indicates whether a specific credential key has a stored value.
/// The actual value is NEVER returned to the browser.
/// </summary>
public sealed record CredentialKeyStatusDto(string Key, bool HasValue);

/// <summary>
/// A discoverable provider module type — returned by the catalogue endpoint.
/// </summary>
public sealed record ProviderModuleCatalogueEntry(
    string ModuleId,
    string DisplayName,
    IReadOnlyList<string> CapabilityNames,
    IReadOnlyList<string> CredentialKeyHints,
    bool RequiresExternalConfig,
    bool AlreadyConfigured);

/// <summary>
/// Request body for creating or updating a provider module configuration.
/// CredentialValues are stored server-side and never returned.
/// </summary>
public sealed record UpsertProviderModuleRequest(
    string ModuleId,
    bool Enabled = true,
    int Priority = 100,
    Dictionary<string, string?>? CredentialValues = null,
    Dictionary<string, string?>? Settings = null);

/// <summary>Result from a create/update/remove operation.</summary>
public sealed record ProviderModuleSetupResult(bool Success, string? Error = null)
{
    public static ProviderModuleSetupResult Ok() => new(true);
    public static ProviderModuleSetupResult Fail(string error) => new(false, error);
}

/// <summary>Result from the test-module endpoint.</summary>
public sealed record ProviderModuleTestResult(
    bool Success,
    bool CredentialConfigValid,
    bool ConnectionVerified,
    string? FailureReason = null,
    double? LatencyMs = null,
    string? ProbeDetail = null);
