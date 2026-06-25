namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Manages provider module configuration lifecycle: discovery, upsert, enable/disable, remove, and connectivity test.
/// </summary>
public interface IProviderModuleSetupService
{
    /// <summary>Returns status of all currently configured provider modules.</summary>
    Task<IReadOnlyList<ProviderModuleStatusDto>> GetConfiguredModulesAsync(CancellationToken ct = default);

    /// <summary>Returns all discoverable provider module types with capability and credential metadata.</summary>
    IReadOnlyList<ProviderModuleCatalogueEntry> GetDiscoveredModuleCatalogue();

    /// <summary>Creates or updates a provider module configuration and persists credentials to the credential store.</summary>
    Task<ProviderModuleSetupResult> UpsertModuleAsync(UpsertProviderModuleRequest request, CancellationToken ct = default);

    /// <summary>Toggles a module's enabled state. Applied live via ProviderRegistry; also persisted to config.</summary>
    Task<ProviderModuleSetupResult> SetEnabledAsync(string moduleId, bool enabled, CancellationToken ct = default);

    /// <summary>Removes a module's configuration from config and its credentials from the credential store.</summary>
    Task<ProviderModuleSetupResult> RemoveModuleAsync(string moduleId, CancellationToken ct = default);

    /// <summary>
    /// Validates stored credentials and optionally probes the live connection.
    /// Always runs ValidateAsync; calls ProbeConnectionAsync if the module implements IProviderModuleConnectionProbe.
    /// </summary>
    Task<ProviderModuleTestResult> TestModuleAsync(string moduleId, CancellationToken ct = default);
}
