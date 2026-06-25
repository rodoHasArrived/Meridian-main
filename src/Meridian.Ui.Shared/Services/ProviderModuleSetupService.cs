using System.Reflection;
using Meridian.Core.Config;
using Meridian.Core.Contracts;
using Meridian.Infrastructure.Adapters.Core;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Manages provider module configuration lifecycle: discovery, upsert, enable/disable, remove, and test.
/// A SemaphoreSlim guards all config load→mutate→save cycles to prevent concurrent write races.
/// </summary>
public sealed class ProviderModuleSetupService : IProviderModuleSetupService
{
    private readonly ConfigStore _configStore;
    private readonly IProviderCredentialStore _credentialStore;
    private readonly ProviderRegistry? _providerRegistry;
    private readonly ILogger<ProviderModuleSetupService> _logger;
    private readonly SemaphoreSlim _configLock = new(1, 1);
    private readonly IReadOnlyList<Type> _moduleTypes;

    public ProviderModuleSetupService(
        ConfigStore configStore,
        IProviderCredentialStore credentialStore,
        ILogger<ProviderModuleSetupService> logger,
        ProviderRegistry? providerRegistry = null)
    {
        _configStore = configStore;
        _credentialStore = credentialStore;
        _logger = logger;
        _providerRegistry = providerRegistry;
        _moduleTypes = DiscoverModuleTypes();
    }

    public async Task<IReadOnlyList<ProviderModuleStatusDto>> GetConfiguredModulesAsync(CancellationToken ct = default)
    {
        var cfg = _configStore.Load();
        var configuredModules = cfg.ProviderModules?.Modules ?? new Dictionary<string, ProviderModuleSettings>();
        var result = new List<ProviderModuleStatusDto>();

        foreach (var (moduleId, settings) in configuredModules)
        {
            var module = CreateModuleById(moduleId);

            var storedKeys = await _credentialStore.GetStoredKeyNamesAsync(moduleId, ct).ConfigureAwait(false);

            // Also treat env-var-mapped credentials as present when the env var is set
            IReadOnlySet<string> resolvedKeys = storedKeys;
            if (settings.Credentials is { } envMap)
            {
                var augmented = new HashSet<string>(storedKeys, StringComparer.OrdinalIgnoreCase);
                foreach (var (key, envVarName) in envMap)
                {
                    if (!string.IsNullOrWhiteSpace(envVarName)
                        && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(envVarName)))
                        augmented.Add(key);
                }
                resolvedKeys = augmented;
            }

            var hintKeys = (module as IProviderModuleCredentialHints)?.CredentialKeyHints ?? [];
            var credStatus = BuildCredentialStatus(hintKeys, resolvedKeys);

            var isRunning = _providerRegistry?.GetAllProviders()
                .Any(p => string.Equals(p.Name, moduleId, StringComparison.OrdinalIgnoreCase) && p.IsEnabled)
                ?? false;

            result.Add(new ProviderModuleStatusDto(
                ModuleId: moduleId,
                DisplayName: module?.ModuleDisplayName ?? moduleId,
                Enabled: settings.Enabled,
                Priority: settings.Priority,
                IsConfigured: true,
                IsRunning: isRunning,
                CredentialStatus: credStatus,
                CapabilityNames: module?.Capabilities.SelectMany(GetCapabilityNames).Distinct().ToList() ?? []));
        }

        return result;
    }

    public IReadOnlyList<ProviderModuleCatalogueEntry> GetDiscoveredModuleCatalogue()
    {
        var cfg = _configStore.Load();
        var configured = cfg.ProviderModules?.Modules?.Keys
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var entries = new List<ProviderModuleCatalogueEntry>();
        foreach (var type in _moduleTypes)
        {
            var m = TryCreateModule(type);
            if (m is null)
                continue;
            entries.Add(new ProviderModuleCatalogueEntry(
                ModuleId: m.ModuleId,
                DisplayName: m.ModuleDisplayName,
                CapabilityNames: m.Capabilities.SelectMany(GetCapabilityNames).Distinct().ToList(),
                CredentialKeyHints: (m as IProviderModuleCredentialHints)?.CredentialKeyHints ?? [],
                RequiresExternalConfig: m.RequiresExternalConfig,
                AlreadyConfigured: configured.Contains(m.ModuleId)));
        }
        return entries;
    }

    public async Task<ProviderModuleSetupResult> UpsertModuleAsync(
        UpsertProviderModuleRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.ModuleId))
            return ProviderModuleSetupResult.Fail("ModuleId is required");

        // Capture pre-mutation state so we can roll back the config entry if the subsequent
        // credential write fails (keeps config and credential store in sync).
        var preMutationCfg = _configStore.Load();
        ProviderModuleSettings? preExistingSettings = null;
        preMutationCfg.ProviderModules?.Modules?.TryGetValue(request.ModuleId, out preExistingSettings);

        // Save config first; only persist credentials if config write succeeds,
        // so a failed config save never leaves orphaned credentials on disk.
        var result = await MutateConfigAsync(cfg =>
        {
            var modules = cfg.ProviderModules?.Modules is not null
                ? new Dictionary<string, ProviderModuleSettings>(cfg.ProviderModules.Modules, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, ProviderModuleSettings>(StringComparer.OrdinalIgnoreCase);

            modules.TryGetValue(request.ModuleId, out var existingSettings);

            var settings = new ProviderModuleSettings(
                Enabled: request.Enabled,
                Priority: request.Priority,
                Credentials: existingSettings?.Credentials,
                Settings: request.Settings is { Count: > 0 }
                    ? new Dictionary<string, string?>(request.Settings, StringComparer.OrdinalIgnoreCase)
                    : existingSettings?.Settings);

            modules[request.ModuleId] = settings;

            return cfg with { ProviderModules = new ProviderModulesConfig(modules) };
        }, ct).ConfigureAwait(false);

        if (!result.Success)
            return result;

        if (request.CredentialValues is { Count: > 0 } creds)
        {
            var existing = await _credentialStore.GetCredentialsAsync(request.ModuleId, ct).ConfigureAwait(false);
            var merged = new Dictionary<string, string>(existing, StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in creds)
            {
                if (string.IsNullOrEmpty(value))
                    merged.Remove(key);
                else
                    merged[key] = value;
            }
            try
            {
                await _credentialStore.SaveCredentialsAsync(request.ModuleId, merged, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Credential save failed for {ModuleId}; attempting config rollback",
                    request.ModuleId.Replace('\n', ' ').Replace('\r', ' '));
                await RollbackModuleConfigAsync(request.ModuleId, preExistingSettings).ConfigureAwait(false);
                return ProviderModuleSetupResult.Fail("Failed to save credentials; the configuration change was rolled back.");
            }
        }

        return result;
    }

    public async Task<ProviderModuleSetupResult> SetEnabledAsync(
        string moduleId,
        bool enabled,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);

        // Persist first; only update live registry when config write succeeds to keep
        // the in-process state and the persisted config in sync across restarts.
        var result = await MutateConfigAsync(cfg =>
        {
            var modules = cfg.ProviderModules?.Modules is not null
                ? new Dictionary<string, ProviderModuleSettings>(cfg.ProviderModules.Modules, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, ProviderModuleSettings>(StringComparer.OrdinalIgnoreCase);

            if (modules.TryGetValue(moduleId, out var existing))
                modules[moduleId] = existing with { Enabled = enabled };

            return cfg with { ProviderModules = new ProviderModulesConfig(modules) };
        }, ct).ConfigureAwait(false);

        if (!result.Success)
            return result;

        if (_providerRegistry is not null)
        {
            if (enabled)
                _providerRegistry.Enable(moduleId);
            else
                _providerRegistry.Disable(moduleId);
        }

        return result;
    }

    public async Task<ProviderModuleSetupResult> RemoveModuleAsync(string moduleId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);

        // Capture settings before removal so we can restore them if credential deletion fails.
        var preDeletionCfg = _configStore.Load();
        ProviderModuleSettings? removedSettings = null;
        preDeletionCfg.ProviderModules?.Modules?.TryGetValue(moduleId, out removedSettings);

        // Remove config first; if SaveAsync fails, credentials remain intact for the still-configured provider
        var result = await MutateConfigAsync(cfg =>
        {
            if (cfg.ProviderModules?.Modules is null)
                return cfg;

            var modules = new Dictionary<string, ProviderModuleSettings>(
                cfg.ProviderModules.Modules, StringComparer.OrdinalIgnoreCase);
            modules.Remove(moduleId);

            return cfg with { ProviderModules = new ProviderModulesConfig(modules) };
        }, ct).ConfigureAwait(false);

        if (!result.Success)
            return result;

        try
        {
            await _credentialStore.DeleteCredentialsAsync(moduleId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Credential deletion failed for {ModuleId}; attempting config rollback",
                moduleId.Replace('\n', ' ').Replace('\r', ' '));
            await RollbackModuleConfigAsync(moduleId, removedSettings).ConfigureAwait(false);
            return ProviderModuleSetupResult.Fail("Failed to delete credentials; the configuration change was rolled back.");
        }

        return result;
    }

    public async Task<ProviderModuleTestResult> TestModuleAsync(string moduleId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);

        var module = CreateModuleById(moduleId);

        if (module is null)
            return new ProviderModuleTestResult(false, false, false, $"Module '{moduleId}' not found");

        var storedCreds = await _credentialStore.GetCredentialsAsync(moduleId, ct).ConfigureAwait(false);

        // Merge env-var-mapped credentials and saved settings from config,
        // matching the same resolution as BuildModuleContexts at startup.
        var cfgForModule = _configStore.Load();
        ProviderModuleSettings? moduleCfg = null;
        cfgForModule.ProviderModules?.Modules?.TryGetValue(moduleId, out moduleCfg);
        var merged = new Dictionary<string, string?>(storedCreds, StringComparer.OrdinalIgnoreCase);
        if (moduleCfg?.Credentials is { } envMap)
        {
            foreach (var (key, envVarName) in envMap)
            {
                if (!merged.ContainsKey(key) && !string.IsNullOrWhiteSpace(envVarName))
                {
                    var envVal = Environment.GetEnvironmentVariable(envVarName);
                    if (!string.IsNullOrWhiteSpace(envVal))
                        merged[key] = envVal;
                }
            }
        }

        var context = new ProviderModuleContext
        {
            Credentials = merged,
            Settings = moduleCfg?.Settings
                ?? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        };
        module.Configure(context);

        var validation = await module.ValidateAsync(ct).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            return new ProviderModuleTestResult(
                Success: false,
                CredentialConfigValid: false,
                ConnectionVerified: false,
                FailureReason: validation.FailureReason);
        }

        if (module is IProviderModuleConnectionProbe probe)
        {
            try
            {
                var probeResult = await probe.ProbeConnectionAsync(ct).ConfigureAwait(false);
                return new ProviderModuleTestResult(
                    Success: probeResult.Connected,
                    CredentialConfigValid: true,
                    ConnectionVerified: probeResult.Connected,
                    FailureReason: probeResult.FailureReason,
                    LatencyMs: probeResult.LatencyMs,
                    ProbeDetail: probeResult.ProbeDetail);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Connection probe threw for module {ModuleId}", moduleId.Replace('\n', ' ').Replace('\r', ' '));
                return new ProviderModuleTestResult(false, true, false, ex.Message);
            }
        }

        return new ProviderModuleTestResult(
            Success: true,
            CredentialConfigValid: true,
            ConnectionVerified: false,
            ProbeDetail: "Credentials valid — no live connection probe available for this provider");
    }

    // ------------------------------------------------------------------
    // Private helpers
    // ------------------------------------------------------------------

    private async Task RollbackModuleConfigAsync(string moduleId, ProviderModuleSettings? preExistingSettings)
    {
        try
        {
            await MutateConfigAsync(cfg =>
            {
                var modules = cfg.ProviderModules?.Modules is not null
                    ? new Dictionary<string, ProviderModuleSettings>(cfg.ProviderModules.Modules, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, ProviderModuleSettings>(StringComparer.OrdinalIgnoreCase);

                if (preExistingSettings is null)
                    modules.Remove(moduleId);
                else
                    modules[moduleId] = preExistingSettings;

                return cfg with { ProviderModules = new ProviderModulesConfig(modules) };
            }, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception rollbackEx)
        {
            _logger.LogError(rollbackEx, "Config rollback failed for {ModuleId}; state may be inconsistent",
                moduleId.Replace('\n', ' ').Replace('\r', ' '));
        }
    }

    private async Task<ProviderModuleSetupResult> MutateConfigAsync(
        Func<AppConfig, AppConfig> mutate,
        CancellationToken ct)
    {
        await _configLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var cfg = _configStore.Load();
            var next = mutate(cfg);
            await _configStore.SaveAsync(next).ConfigureAwait(false);
            return ProviderModuleSetupResult.Ok();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mutate provider module config");
            return ProviderModuleSetupResult.Fail(ex.Message);
        }
        finally
        {
            _configLock.Release();
        }
    }

    private static IReadOnlyList<CredentialKeyStatusDto> BuildCredentialStatus(
        IReadOnlyList<string> hintKeys,
        IReadOnlySet<string> storedKeys)
    {
        if (hintKeys.Count > 0)
        {
            return hintKeys.Select(k => new CredentialKeyStatusDto(k, storedKeys.Contains(k))).ToList();
        }

        return storedKeys.Select(k => new CredentialKeyStatusDto(k, true)).ToList();
    }

    private IProviderModule? CreateModuleById(string moduleId)
    {
        var type = _moduleTypes.FirstOrDefault(t =>
        {
            var m = TryCreateModule(t);
            return m is not null && string.Equals(m.ModuleId, moduleId, StringComparison.OrdinalIgnoreCase);
        });
        return type is null ? null : TryCreateModule(type);
    }

    private static IProviderModule? TryCreateModule(Type type)
    {
        try
        {
            return Activator.CreateInstance(type) as IProviderModule;
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<string> GetCapabilityNames(ProviderCapabilities cap)
    {
        if (cap.SupportsStreaming)
            yield return "Streaming";
        if (cap.SupportsBackfill)
            yield return "Backfill";
        if (cap.SupportsSymbolSearch)
            yield return "Symbol Search";
        if (cap.SupportsBrokerage)
            yield return "Brokerage";
        if (cap.SupportsOptionsChain)
            yield return "Options Chain";
    }

    private static IReadOnlyList<Type> DiscoverModuleTypes()
    {
        var types = new List<Type>();
        var assemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic);

        foreach (var assembly in assemblies)
        {
            IEnumerable<Type> assemblyTypes;
            try
            {
                assemblyTypes = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                assemblyTypes = ex.Types.Where(t => t is not null)!;
            }

            foreach (var type in assemblyTypes)
            {
                if (type.IsAbstract || type.IsInterface)
                    continue;
                if (!typeof(IProviderModule).IsAssignableFrom(type))
                    continue;

                // Verify the type can be instantiated (no ctor args required)
                if (type.GetConstructor(Type.EmptyTypes) is not null)
                    types.Add(type);
            }
        }

        return types;
    }
}
