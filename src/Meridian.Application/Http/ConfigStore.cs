using System.Text.Json;
using System.Text.Json.Nodes;
using Meridian.Application.ProviderRouting;
using Meridian.Application.Backfill;
using Meridian.Core.Config;
using Meridian.Core.IO;
using Meridian.Core.Logging;
using Meridian.DataIntegration.Monitoring;
using Meridian.Contracts.Configuration;
using Meridian.Infrastructure.Contracts;
using Meridian.Storage;
using Meridian.Storage.Archival;
using Meridian.Contracts.Backfill;
using Meridian.Storage.Backfill;

namespace Meridian.Application.UI;

/// <summary>
/// Service for loading and persisting application configuration.
/// Provides access to config files, status files, and provider metrics.
/// This is the single implementation shared by console, web, and desktop hosts.
/// </summary>
/// <remarks>
/// <para><b>Configuration Path Resolution:</b></para>
/// <list type="bullet">
/// <item><description>If configPath is provided, it is used directly</description></item>
/// <item><description>If configPath is null, uses PathResolver delegate if set</description></item>
/// <item><description>Otherwise searches the current directory and its ancestors for config/appsettings.json or appsettings.json</description></item>
/// </list>
/// </remarks>
[ImplementsAdr("ADR-001", "Consolidated configuration store shared by all hosts")]
public sealed class ConfigStore
{
    /// <summary>
    /// Default path resolver that returns the nearest standard Meridian configuration path.
    /// </summary>
    public static Func<string> DefaultPathResolver { get; set; } = ResolveDefaultPath;

    private static readonly TimeSpan WriteLockTimeout = TimeSpan.FromSeconds(30);
    // Resolved per use so loads after logging bootstrap reach the configured sinks.
    private static Serilog.ILogger Log => LoggingSetup.ForContext<ConfigStore>();
    private readonly ProviderCredentialOwnershipPolicy _ownershipPolicy;
    private readonly ConfigurationRevisionJournal _revisions;

    /// <summary>
    /// Gets the path to the configuration file.
    /// </summary>
    public string ConfigPath { get; }

    /// <summary>
    /// Creates a new ConfigStore with the default configuration path resolution.
    /// </summary>
    public ConfigStore()
        : this(null) { }

    /// <summary>
    /// Creates a new ConfigStore with a custom configuration path.
    /// </summary>
    /// <param name="configPath">Full or relative path to the configuration file.</param>
    public ConfigStore(string? configPath, ProviderCredentialOwnershipPolicy? ownershipPolicy = null)
    {
        ConfigPath = ResolvePath(configPath);
        _revisions = new ConfigurationRevisionJournal(ConfigPath);
        _ownershipPolicy = ownershipPolicy ?? ProviderCredentialOwnershipPolicy.ForCurrentEnvironment();
    }

    private static string ResolvePath(string? configPath)
    {
        var path = configPath ?? DefaultPathResolver();
        return Path.IsPathRooted(path) ? path : Path.GetFullPath(path);
    }

    private static string ResolveDefaultPath()
        => DefaultConfigPathResolver.Resolve();

    /// <summary>
    /// Static method to load configuration from a path.
    /// Used by ConfigurationService.LoadAndPrepareConfig() for consolidated config loading.
    /// </summary>
    /// <remarks>
    /// A missing file yields the default configuration (with a logged warning) so first-run hosts can
    /// start. A file that exists but cannot be read or parsed fails closed: substituting defaults for
    /// an authoritative document the operator wrote would silently change trading behavior.
    /// </remarks>
    /// <exception cref="InvalidDataException">The file exists but could not be read or parsed.</exception>
    public static AppConfig LoadConfig(string path)
    {
        if (!File.Exists(path))
        {
            Log.Warning(
                "Configuration file not found at {ConfigPath}; using default configuration. Copy config/appsettings.sample.json to config/appsettings.json to customize",
                path);
            return CreateDefaultConfig(path);
        }

        try
        {
            var json = File.ReadAllText(path);
            var config = JsonSerializer.Deserialize<AppConfig>(json, AppConfigJsonOptions.Read)
                ?? throw new InvalidDataException("Configuration document is JSON null.");
            return NormalizeLoadedConfig(path, json, config);
        }
        catch (Exception ex) when (ex is JsonException or IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException)
        {
            Log.Error(ex, "Configuration file {ConfigPath} exists but could not be loaded; refusing to fall back to defaults", path);
            throw new InvalidDataException($"Configuration file '{path}' exists but could not be loaded: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Instance method to load configuration from the configured path.
    /// Delegates to the static LoadConfig method.
    /// </summary>
    public AppConfig Load() => LoadConfig(ConfigPath);

    /// <summary>Saved setting impacts and exact-revision collector evidence; contains no setting values.</summary>
    public ConfigurationChangeStatus GetChangeStatus() => ProjectChangeStatus(isSaveResponse: false);

    private ConfigurationChangeStatus ProjectChangeStatus(bool isSaveResponse)
    {
        var local = ConfigurationChangeTracker.ForPath(ConfigPath).Status;
        var saved = _revisions.ReadCurrent();
        if (saved is null)
        {
            return _revisions.Exists || local.Revision is not null
                ? local with { Revision = null, LastApply = null, RuntimeState = ConfigurationRuntimeState.Unknown }
                : local;
        }

        var localIsCurrent = local.Revision == saved.Revision;
        var acknowledgement = _revisions.ActiveAcknowledgement(saved);
        var localApply = localIsCurrent ? local.LastApply : null;
        var collectorApply = acknowledgement?.Entries ?? [];
        var settled = collectorApply.Where(entry => ConfigurationRevisionJournal.HasTakenEffect(entry.Kind))
            .Select(entry => entry.Path).ToHashSet(StringComparer.Ordinal);
        // Preserve confirmations from non-collector appliers in the process that obtained them.
        // A stopped collector's earlier result cannot remain in the shared status as a live result.
        var localEntries = (localApply?.Entries ?? [])
            .Where(entry => !ConfigurationRevisionJournal.IsCollectorPath(entry.Path) || saved.Acknowledgement is null)
            .ToArray();
        settled.UnionWith(localEntries.Where(entry => ConfigurationRevisionJournal.HasTakenEffect(entry.Kind))
            .Select(entry => entry.Path));
        var entries = localEntries.Concat(collectorApply).ToArray();
        var pending = local.PendingChanges.Where(change => !ConfigurationRevisionJournal.IsCollectorPath(change.Path))
            .Concat(saved.CollectorPaths.Where(path => !settled.Contains(path))
                .Select(path => new ConfigurationFieldChange(path, ConfigurationChangeTracker.Classify(path))))
            .DistinctBy(change => change.Path, StringComparer.Ordinal)
            .ToArray();
        var changes = (localIsCurrent ? local.Changes : saved.Changes)
            .Select(change => settled.Contains(change.Path) ? change with { Impact = ConfigurationChangeImpact.Live } : change)
            .ToArray();

        var state = RuntimeStateFor(saved, acknowledgement, isSaveResponse);

        return new ConfigurationChangeStatus(
            changes.Length == 0 ? ConfigurationChangeImpact.Live : changes.Max(change => change.Impact),
            changes, pending, saved.CommittedAtUtc, local.CanReconnectInProcess,
            LastApply: entries.Length == 0 ? null : new(entries, acknowledgement?.AppliedAtUtc ?? localApply!.AppliedAtUtc),
            Revision: saved.Revision, RuntimeState: state, CollectorAcknowledgement: acknowledgement);
    }

    private static ConfigurationRuntimeState? RuntimeStateFor(
        ConfigurationRevisionRecord saved, ConfigurationRuntimeAcknowledgement? acknowledgement, bool isSaveResponse)
    {
        if (saved.CollectorPaths.Count == 0)
            return null;
        if (acknowledgement is null)
            return isSaveResponse ? ConfigurationRuntimeState.Saved : ConfigurationRuntimeState.Unknown;
        if (acknowledgement.Entries.Any(entry => entry.Kind == ConfigurationApplyKind.Failed))
            return ConfigurationRuntimeState.Failed;
        if (acknowledgement.Entries.Any(entry => entry.Kind == ConfigurationApplyKind.Deferred))
            return ConfigurationRuntimeState.Deferred;
        if (acknowledgement.Entries.Count < saved.CollectorPaths.Count)
            return ConfigurationRuntimeState.Unknown;
        return acknowledgement.Entries.Any(entry => entry.Kind is ConfigurationApplyKind.AppliedLive or ConfigurationApplyKind.ProviderReconfigured)
            ? ConfigurationRuntimeState.AppliedLive : ConfigurationRuntimeState.Saved;
    }

    /// <summary>Conservative activation requirement for a configuration field, also exposed by effective settings.</summary>
    public static ConfigurationChangeImpact GetSettingImpact(string path) => ConfigurationChangeTracker.Classify(path);

    /// <summary>Loads existing configuration without substituting defaults when authoritative state cannot be read.</summary>
    public AppConfig LoadRequired() => LoadRequired(File.ReadAllText(ConfigPath));

    internal AppConfig LoadRequired(string json)
    {
        var config = JsonSerializer.Deserialize<AppConfig>(json, AppConfigJsonOptions.Read)
            ?? throw new InvalidDataException("Required configuration is empty.");
        return NormalizeLoadedConfig(ConfigPath, json, config);
    }

    // The callback must be synchronous and must not reenter configuration persistence.
    internal async Task<T> UpdateRequiredAsync<T>(Func<AppConfig, (AppConfig Config, T Result)> update, CancellationToken ct = default)
        => (await UpdateCoreAsync(LoadRequired, update, ct).ConfigureAwait(false)).Result;

    // Vault checks and template publication share the same ownership lease as credential writes.
    internal Task<T> UpdateRequiredWithLeaseAsync<T>(
        Func<AppConfig, CancellationToken, Task<(AppConfig Config, T Result)>> update, CancellationToken ct)
        => UpdateRequiredCheckedAsync(update, ct);

    /// <summary>
    /// Applies a checked update to persisted configuration. The callback may read local evidence
    /// under the same lock as the commit. It must not perform network I/O or reenter configuration writes.
    /// </summary>
    public async Task<T> UpdateRequiredCheckedAsync<T>(
        Func<AppConfig, CancellationToken, Task<(AppConfig Config, T Result)>> update,
        CancellationToken ct = default)
        => (await UpdateRequiredCheckedWithChangeStatusAsync(update, ct).ConfigureAwait(false)).Result;

    /// <summary>Returns the checked update's result and revision status before releasing the write lease.</summary>
    internal Task<(T Result, ConfigurationChangeStatus Status)> UpdateRequiredCheckedWithChangeStatusAsync<T>(
        Func<AppConfig, CancellationToken, Task<(AppConfig Config, T Result)>> update, CancellationToken ct)
        => UpdateCoreAsync(LoadRequired, update, ct);

    /// <summary>
    /// Read-modify-write of the configuration document under the cross-process configuration lock
    /// (PRD-002: concurrent configuration writes). The current document is loaded <em>inside</em> the
    /// lock, handed to <paramref name="update"/>, and the returned document is written atomically
    /// before the lock is released, so two writers editing different sections can never lose each
    /// other's change the way a <c>Load()</c> + <see cref="SaveAsync"/> pair can.
    /// </summary>
    /// <remarks>
    /// <para>A missing file starts from the default configuration, matching <see cref="Load"/>. An
    /// unreadable or JSON-<c>null</c> file throws and leaves the file untouched: an edit never
    /// replaces a corrupt authoritative document with defaults. Returning the same reference skips
    /// the write. The callback must be synchronous and must not reenter configuration persistence.</para>
    /// </remarks>
    public async Task<T> UpdateAsync<T>(Func<AppConfig, (AppConfig Config, T Result)> update, CancellationToken ct = default)
        => (await UpdateWithChangeStatusAsync(update, ct).ConfigureAwait(false)).Result;

    /// <summary>Returns the result and the exact committed revision's status before releasing the write lease.</summary>
    internal Task<(T Result, ConfigurationChangeStatus Status)> UpdateWithChangeStatusAsync<T>(
        Func<AppConfig, (AppConfig Config, T Result)> update, CancellationToken ct = default)
        => UpdateCoreAsync(LoadForUpdate, update, ct);

    /// <summary>Result-less form of <see cref="UpdateAsync{T}"/>.</summary>
    public Task UpdateAsync(Func<AppConfig, AppConfig> update, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        return UpdateCoreAsync<object?>(LoadForUpdate, current => (update(current), null), ct);
    }

    private Task<(T Result, ConfigurationChangeStatus Status)> UpdateCoreAsync<T>(
        Func<AppConfig> load,
        Func<AppConfig, (AppConfig Config, T Result)> update,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(update);
        return UpdateCoreAsync(load, (current, _) => Task.FromResult(update(current)), ct);
    }

    private async Task<(T Result, ConfigurationChangeStatus Status)> UpdateCoreAsync<T>(
        Func<AppConfig> load,
        Func<AppConfig, CancellationToken, Task<(AppConfig Config, T Result)>> update,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(update);
        using var lease = await AcquireWriteLockAsync(ct).ConfigureAwait(false);
        var current = load();
        var (next, result) = await update(current, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        var changed = !ReferenceEquals(current, next);
        if (changed)
            await WriteAndApplyAsync(current, next, ct).ConfigureAwait(false);

        return (result, ProjectChangeStatus(isSaveResponse: changed));
    }

    private AppConfig LoadForUpdate()
        => File.Exists(ConfigPath) ? LoadRequired() : CreateDefaultConfig(ConfigPath);

    /// <summary>
    /// Holds the cross-process configuration write lock (the <c>.lock</c> sidecar beside the file)
    /// until the returned lease is disposed. For writers that persist the document through their own
    /// serializer, such as the WPF shell's configuration service, so the installed two-process
    /// workstation shares one lock with every <see cref="UpdateAsync{T}"/> caller. Not reentrant.
    /// </summary>
    public async Task<IDisposable> AcquireWriteLeaseAsync(CancellationToken ct = default)
        => await AcquireWriteLockAsync(ct).ConfigureAwait(false);

    private async Task<FileStream> AcquireWriteLockAsync(CancellationToken ct)
    {
        try
        {
            // Keep the sidecar: deleting it could allow two holders on different inodes.
            return await ExclusiveFileLock.AcquireAsync(ConfigPath + ".lock", WriteLockTimeout, ct).ConfigureAwait(false);
        }
        catch (TimeoutException error)
        {
            throw new TimeoutException("Timed out waiting for the configuration write lock.", error.InnerException);
        }
    }

    /// <summary>
    /// Whole-document replacement: the caller's snapshot becomes the configuration, last writer wins.
    /// Reserved for callers that own the entire document (the CLI "save this configuration" path and
    /// the setup wizard's generated output). Section-level edits use <see cref="UpdateAsync{T}"/>,
    /// which reads the current document under the same lock and cannot drop a concurrent change.
    /// </summary>
    public async Task SaveAsync(AppConfig cfg, CancellationToken ct = default)
    {
        var snapshot = ProviderConfigurationCredentialGuard.Prepare(cfg, _ownershipPolicy);
        using var lease = await AcquireWriteLockAsync(ct).ConfigureAwait(false);
        await WriteAndApplyAsync(LoadForUpdate(), snapshot, ct).ConfigureAwait(false);
    }

    private async Task WriteAndApplyAsync(AppConfig current, AppConfig next, CancellationToken ct)
    {
        var validated = ProviderConfigurationCredentialGuard.Prepare(next, _ownershipPolicy);
        var json = JsonSerializer.Serialize(validated, AppConfigJsonOptions.Write);
        // Apply the same bytes used for the revision hash, including for in-process callers
        // whose configuration model contains mutable arrays or extension dictionaries.
        var snapshot = LoadRequired(json);
        var changes = ConfigurationChangeTracker.Changes(current, snapshot);
        var collectorChanges = ConfigurationChangeTracker.CollectorChanges(current, snapshot);
        var staged = await _revisions.PrepareAsync(json, collectorChanges, ct).ConfigureAwait(false);
        await SaveCoreAsync(json, ct).ConfigureAwait(false);
        var committed = await _revisions.ConfirmSaveAsync(staged).ConfigureAwait(false);
        var applied = await ApplyToRuntimeAsync(current, snapshot, changes, committed, ct).ConfigureAwait(false);
        ConfigurationChangeTracker.ForPath(ConfigPath).Record(current, snapshot, applied, committed?.Revision);
    }

    /// <summary>
    /// Offers a durably saved document to the runtime appliers attached to this file. The save has already
    /// succeeded, so applier failures and cancellation become runtime outcomes instead of failing the save.
    /// </summary>
    private async Task<IReadOnlyList<ConfigurationApplyEntry>?> ApplyToRuntimeAsync(
        AppConfig previous,
        AppConfig next,
        IReadOnlyList<ConfigurationFieldChange> changes,
        ConfigurationRevisionRecord? committed,
        CancellationToken ct)
    {
        var appliers = ConfigurationRuntimeAppliers.For(ConfigPath);
        if (appliers.Count == 0)
            return null;

        var changedPaths = changes.Select(change => change.Path).ToArray();

        var entries = new List<ConfigurationApplyEntry>();
        foreach (var applier in appliers)
        {
            var recordsCollectorEvidence = applier is CollectorConfigurationRuntime;
            var paths = recordsCollectorEvidence && committed is not null ? committed.CollectorPaths : changedPaths;
            var handled = paths.Where(applier.Handles).ToArray();
            if (handled.Length == 0)
                continue;
            if (applier.ReconcilePendingPaths)
            {
                handled = handled.Concat(GetChangeStatus().PendingChanges.Select(change => change.Path).Where(applier.Handles))
                    .Distinct(StringComparer.Ordinal).ToArray();
            }

            try
            {
                var result = await applier.ApplyAsync(previous, next, handled, ct).ConfigureAwait(false);
                if (!recordsCollectorEvidence)
                    entries.AddRange(result.Where(entry => handled.Contains(entry.Path, StringComparer.Ordinal)));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                Log.Warning("Runtime apply by {Applier} was cancelled after the configuration was saved; {PathCount} setting(s) need a restart",
                    applier.Name, handled.Length);
                if (!recordsCollectorEvidence)
                    entries.AddRange(handled.Select(path => new ConfigurationApplyEntry(path, ConfigurationApplyKind.Deferred,
                        "The runtime apply was cancelled; restart the host to use the saved value.")));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Runtime apply by {Applier} failed after the configuration was saved; {PathCount} setting(s) need a restart",
                    applier.Name, handled.Length);
                if (!recordsCollectorEvidence)
                    entries.AddRange(handled.Select(path => new ConfigurationApplyEntry(path, ConfigurationApplyKind.Failed,
                        "The running host could not apply this change; restart the host to use the saved value.")));
            }
        }

        return entries.Count == 0 ? null : entries;
    }

    private async Task SaveCoreAsync(string json, CancellationToken ct)
    {
        try
        {
            await AtomicFileWriter.WriteAsync(ConfigPath, json, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to save configuration: {ex.Message}", ex);
        }
    }

    public async Task SaveFeatureCapabilityOverridesAsync(
        IReadOnlyDictionary<string, bool> overrides,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        using var lease = await AcquireWriteLockAsync(ct).ConfigureAwait(false);

        try
        {
            var directory = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            JsonObject root;
            if (File.Exists(ConfigPath))
            {
                var json = await File.ReadAllTextAsync(ConfigPath, ct).ConfigureAwait(false);
                root = ParseConfigRoot(json);
            }
            else
            {
                root = new JsonObject();
            }

            var section = root["FeatureCapabilities"] as JsonObject ?? new JsonObject();
            var persistedOverrides = new JsonObject();

            foreach (var entry in overrides.OrderBy(static entry => entry.Key, StringComparer.OrdinalIgnoreCase))
            {
                persistedOverrides[entry.Key] = entry.Value;
            }

            section["Overrides"] = persistedOverrides;
            root["FeatureCapabilities"] = section;

            var output = root.ToJsonString(AppConfigJsonOptions.Write);
            using var candidate = JsonDocument.Parse(output);
            ProviderConfigurationCredentialGuard.Validate(candidate.RootElement, _ownershipPolicy);
            await AtomicFileWriter.WriteAsync(ConfigPath, output, ct).ConfigureAwait(false);
        }
        catch (ProviderConfigurationCredentialException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to save feature capability overrides: {ex.Message}", ex);
        }
    }

    public string? TryLoadStatusJson()
    {
        try
        {
            var statusPath = GetStatusPath();
            return File.Exists(statusPath) ? File.ReadAllText(statusPath) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public string GetStatusPath(AppConfig? cfg = null)
    {
        cfg ??= Load();
        var root = GetDataRoot(cfg);
        return Path.Combine(root, "_status", "status.json");
    }

    public string GetBackfillStatusPath(AppConfig? cfg = null)
    {
        cfg ??= Load();
        var root = GetDataRoot(cfg);
        return Path.Combine(root, "_status", "backfill.json");
    }

    public BackfillResult? TryLoadBackfillStatus()
    {
        var cfg = Load();
        var store = new BackfillStatusStore(GetDataRoot(cfg));
        return store.TryRead();
    }

    public string GetDataRoot(AppConfig? cfg = null)
    {
        cfg ??= Load();
        return MeridianPathDefaults.ResolveDataRoot(ConfigPath, cfg.DataRoot);
    }

    public string GetProviderMetricsPath(AppConfig? cfg = null)
    {
        cfg ??= Load();
        var root = GetDataRoot(cfg);
        return Path.Combine(root, "_status", "providers.json");
    }

    public ProviderMetricsStatus? TryLoadProviderMetrics()
    {
        try
        {
            var path = GetProviderMetricsPath();
            if (!File.Exists(path))
                return null;

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<ProviderMetricsStatus>(json, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static AppConfig CreateDefaultConfig(string path)
        => new(DataRoot: MeridianPathDefaults.ResolveDataRoot(path, null));

    private static JsonObject ParseConfigRoot(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new JsonObject();
        }

        var documentOptions = new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        };

        var node = JsonNode.Parse(json, documentOptions: documentOptions);
        return node as JsonObject ?? new JsonObject();
    }

    private static AppConfig NormalizeLoadedConfig(string path, string json, AppConfig? config)
    {
        var configuredDataRoot = MeridianPathDefaults.ResolveConfiguredDataRootFromJson(
            json,
            config?.DataRoot);

        var resolvedDataRoot = MeridianPathDefaults.ResolveDataRoot(path, configuredDataRoot);
        var effectiveConfig = config ?? new AppConfig();

        return effectiveConfig with { DataRoot = resolvedDataRoot };
    }
}
