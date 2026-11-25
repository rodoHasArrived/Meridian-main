using Meridian.Application.UI;
using Meridian.Contracts.Configuration;
using Meridian.Core.Config;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Adapters.Failover;
using Serilog;

namespace Meridian.Application.Composition.Startup;

/// <summary>
/// Applies saved <c>DataSources</c> edits to a running collector without a restart, and reports
/// exactly what happened to each changed setting.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>Failover rule thresholds, latency limits, backup lists (limited to providers with a
/// live client) and the health-check interval apply live through <see cref="StreamingFailoverService"/>.</description></item>
/// <item><description>Enabling or disabling a source that has a live client excludes it from (or re-includes it
/// in) failover; disabling the active provider hands its subscriptions to the next enabled backup.</description></item>
/// <item><description>Provider kinds, IDs, credentials, endpoints, new rules and the failover master switch are
/// deferred to a host restart. Settings the streaming runtime does not read are reported as having no
/// runtime effect rather than as applied.</description></item>
/// </list>
/// The reconciler diffs against its own last-applied snapshot, so a UI save followed by the file
/// watcher's notification for the same write is applied once.
/// </remarks>
public sealed class ProviderConfigReconciler : IConfigurationRuntimeApplier, IDisposable
{
    internal const string Root = "DataSources";
    private static readonly TimeSpan DefaultHandOffTimeout = TimeSpan.FromSeconds(15);

    private readonly StreamingFailoverService _failover;
    private readonly HashSet<string> _liveProviderIds;
    private readonly ILogger _log;
    private readonly TimeSpan _handOffTimeout;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AppConfig _applied;

    public ProviderConfigReconciler(
        StreamingFailoverService failover,
        IEnumerable<string> liveProviderIds,
        AppConfig startupConfig,
        ILogger log,
        TimeSpan? handOffTimeout = null)
    {
        _failover = failover ?? throw new ArgumentNullException(nameof(failover));
        ArgumentNullException.ThrowIfNull(liveProviderIds);
        _applied = startupConfig ?? throw new ArgumentNullException(nameof(startupConfig));
        _log = (log ?? throw new ArgumentNullException(nameof(log))).ForContext<ProviderConfigReconciler>();
        _liveProviderIds = new HashSet<string>(
            liveProviderIds.Select(ProviderIdentity.NormalizeId),
            StringComparer.OrdinalIgnoreCase);
        _handOffTimeout = handOffTimeout ?? DefaultHandOffTimeout;
    }

    public string Name => nameof(ProviderConfigReconciler);

    // Flattened paths follow the configuration file's JSON naming (camelCase), so match case-insensitively.
    public bool Handles(string path)
        => path.Equals(Root, StringComparison.OrdinalIgnoreCase) || path.StartsWith(Root + ".", StringComparison.OrdinalIgnoreCase);

    /// <summary>Applies a configuration document observed by the file watcher.</summary>
    public async Task<IReadOnlyList<ConfigurationApplyEntry>> ApplyAsync(AppConfig next, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(next);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var paths = ConfigurationChangeTracker.ChangedPaths(_applied, next).Where(Handles).ToArray();
            return paths.Length == 0 ? [] : await ApplyCoreAsync(_applied, next, paths, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ConfigurationApplyEntry>> ApplyAsync(
        AppConfig previous,
        AppConfig next,
        IReadOnlyList<string> changedPaths,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(changedPaths);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Diff against what the runtime actually holds; the saved "previous" may already be applied.
            var pending = new HashSet<string>(
                ConfigurationChangeTracker.ChangedPaths(_applied, next).Where(Handles), StringComparer.Ordinal);
            var applied = pending.Count == 0
                ? []
                : await ApplyCoreAsync(_applied, next, pending.ToArray(), ct).ConfigureAwait(false);
            var byPath = applied.ToDictionary(entry => entry.Path, StringComparer.Ordinal);

            // Paths already live from an earlier apply are reported as applied, not re-run.
            return changedPaths.Where(Handles)
                .Select(path => byPath.TryGetValue(path, out var entry)
                    ? entry
                    : new ConfigurationApplyEntry(path, ConfigurationApplyKind.AppliedLive, "Already applied to the running host."))
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<ConfigurationApplyEntry>> ApplyCoreAsync(
        AppConfig previousConfig,
        AppConfig nextConfig,
        IReadOnlyList<string> paths,
        CancellationToken ct)
    {
        var previous = previousConfig.DataSources ?? new DataSourcesConfig();
        var next = nextConfig.DataSources ?? new DataSourcesConfig();
        var entries = new List<ConfigurationApplyEntry>(paths.Count);

        IReadOnlyDictionary<string, FailoverRuleApplyOutcome>? ruleOutcomes = null;
        var referenceError = FindUnknownRuleProvider(next);
        if (referenceError is null &&
            next.EnableFailover == previous.EnableFailover &&
            paths.Any(path => Segment(path, 1) is "FailoverRules" or "HealthCheckIntervalSeconds"))
        {
            ruleOutcomes = _failover.ApplyConfiguration(next)
                .ToDictionary(outcome => outcome.RuleId, StringComparer.OrdinalIgnoreCase);
        }

        // Sources[*].Enabled edits are applied once per source, then mapped back to each path.
        var sourceOutcomes = new Dictionary<int, ConfigurationApplyEntry>();

        foreach (var path in paths)
        {
            ct.ThrowIfCancellationRequested();
            var section = Segment(path, 1);
            ConfigurationApplyEntry entry = section switch
            {
                "FailoverRules" when referenceError is not null
                    => Deferred(path, referenceError),
                "FailoverRules" when next.EnableFailover != previous.EnableFailover
                    => Deferred(path, "Turning failover on or off takes effect after a host restart."),
                "FailoverRules" => RuleEntry(path, previous, next, ruleOutcomes),
                "HealthCheckIntervalSeconds" when ruleOutcomes is not null
                    => new(path, ConfigurationApplyKind.AppliedLive, "Health checks now run at the saved interval."),
                "FailoverRuleAudit"
                    => new(path, ConfigurationApplyKind.NoRuntimeEffect, "Audit history only."),
                "FailoverTimeoutSeconds" or "AutoRecover" or "DefaultHistoricalSourceId"
                    => new(path, ConfigurationApplyKind.NoRuntimeEffect,
                        "Saved; the streaming failover runtime does not read this setting."),
                "Sources" => await SourceEntryAsync(path, previous, next, sourceOutcomes, ct).ConfigureAwait(false),
                _ => Deferred(path, section switch
                {
                    "EnableFailover" => "Turning failover on or off takes effect after a host restart.",
                    "DefaultRealTimeSourceId" => "Changing the default real-time source takes effect after a host restart.",
                    "SymbolMappings" => "Symbol mapping changes take effect after a host restart.",
                    "HealthCheckIntervalSeconds" => "The failover rules could not be applied; restart the host to use the saved interval.",
                    _ => "This provider setting takes effect after a host restart."
                })
            };
            entries.Add(entry);
        }

        _applied = nextConfig;
        _log.Information(
            "Provider configuration reconciled: {Applied} applied live, {Reconfigured} provider changes, {NoEffect} without runtime effect, {Deferred} deferred to restart",
            entries.Count(e => e.Kind == ConfigurationApplyKind.AppliedLive),
            entries.Count(e => e.Kind == ConfigurationApplyKind.ProviderReconfigured),
            entries.Count(e => e.Kind == ConfigurationApplyKind.NoRuntimeEffect),
            entries.Count(e => e.Kind == ConfigurationApplyKind.Deferred));
        foreach (var deferred in entries.Where(e => e.Kind == ConfigurationApplyKind.Deferred))
            _log.Information("Provider setting {Path} deferred to host restart: {Reason}", deferred.Path, deferred.Detail);

        return entries;
    }

    private static ConfigurationApplyEntry RuleEntry(
        string path,
        DataSourcesConfig previous,
        DataSourcesConfig next,
        IReadOnlyDictionary<string, FailoverRuleApplyOutcome>? outcomes)
    {
        if (outcomes is null)
            return Deferred(path, "The failover rules could not be applied; restart the host to use them.");

        if (Segment(path, 2) is { } indexText && int.TryParse(indexText, out var index))
        {
            var candidates = new[] { At(next.FailoverRules, index)?.Id, At(previous.FailoverRules, index)?.Id }
                .Where(id => id is not null)
                .Select(id => outcomes.TryGetValue(id!, out var outcome) ? outcome : null)
                .Where(outcome => outcome is not null)
                .ToArray();
            if (candidates.FirstOrDefault(outcome => !outcome!.Applied) is { } deferred)
                return Deferred(path, deferred.Reason ?? "This rule change takes effect after a host restart.");
        }
        else if (outcomes.Values.FirstOrDefault(outcome => !outcome.Applied) is { } anyDeferred)
        {
            return Deferred(path, anyDeferred.Reason ?? "This rule change takes effect after a host restart.");
        }

        return new(path, ConfigurationApplyKind.AppliedLive, "The failover coordinator now uses the saved rule.");
    }

    private async Task<ConfigurationApplyEntry> SourceEntryAsync(
        string path,
        DataSourcesConfig previous,
        DataSourcesConfig next,
        Dictionary<int, ConfigurationApplyEntry> sourceOutcomes,
        CancellationToken ct)
    {
        if (Segment(path, 2) is not { } indexText || !int.TryParse(indexText, out var index))
            return Deferred(path, "Adding or removing data sources takes effect after a host restart.");

        var source = At(next.Sources, index);
        var before = At(previous.Sources, index);
        if (source is null || before is null ||
            !ProviderIdentity.EqualsId(source.Id, before.Id) || source.Provider != before.Provider)
        {
            return Deferred(path, "Adding, removing or re-identifying data sources takes effect after a host restart.");
        }

        switch (Segment(path, 3))
        {
            case "Priority":
                return new(path, ConfigurationApplyKind.NoRuntimeEffect,
                    "Saved; streaming failover order comes from the failover rules, not source priority.");
            case "Name" or "Description" or "Tags" or "Type":
                return new(path, ConfigurationApplyKind.NoRuntimeEffect, "Saved; descriptive only for streaming.");
            case "Enabled":
                if (!sourceOutcomes.TryGetValue(index, out var outcome))
                {
                    outcome = await ApplyEnabledAsync(path, source, ct).ConfigureAwait(false);
                    sourceOutcomes[index] = outcome;
                }

                return outcome with { Path = path };
            default:
                return Deferred(path, $"Credential, endpoint and symbol changes for '{source.Id}' take effect after a host restart.");
        }
    }

    private async Task<ConfigurationApplyEntry> ApplyEnabledAsync(string path, DataSourceConfig source, CancellationToken ct)
    {
        var id = ProviderIdentity.NormalizeId(source.Id);
        if (!_liveProviderIds.Contains(id))
        {
            return source.Enabled
                ? Deferred(path, $"'{id}' has no live streaming client in this host; restart the host to start it.")
                : new(path, ConfigurationApplyKind.NoRuntimeEffect, $"'{id}' is not streaming in this host.");
        }

        _failover.SetProviderEnabled(id, source.Enabled);
        if (source.Enabled)
            return new(path, ConfigurationApplyKind.ProviderReconfigured, $"'{id}' is eligible for failover again.");

        var drivenRules = _failover.GetRuleSnapshots()
            .Where(rule => ProviderIdentity.EqualsId(rule.CurrentActiveProviderId, id))
            .Select(rule => rule.RuleId)
            .ToArray();
        foreach (var ruleId in drivenRules)
        {
            using var handOff = CancellationTokenSource.CreateLinkedTokenSource(ct);
            handOff.CancelAfter(_handOffTimeout);
            bool? moved;
            try
            {
                moved = await _failover.MoveOffDisabledProviderAsync(ruleId, handOff.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                moved = false;
            }

            if (moved == false)
            {
                return Deferred(path,
                    $"'{id}' is excluded from failover but is still the active provider for rule '{ruleId}': no enabled backup could take over.");
            }

            var active = _failover.GetActiveProviderId(ruleId);
            _log.Information("Moved failover rule {RuleId} off disabled provider {ProviderId} to {ActiveProviderId}",
                ruleId, id, active);
        }

        return new(path, ConfigurationApplyKind.ProviderReconfigured, drivenRules.Length == 0
            ? $"'{id}' is excluded from failover."
            : $"'{id}' is excluded from failover; streaming moved to '{_failover.GetActiveProviderId(drivenRules[0])}'.");
    }

    /// <summary>Returns a reason when a rule names a provider that is not a configured source.</summary>
    private static string? FindUnknownRuleProvider(DataSourcesConfig config)
    {
        foreach (var rule in config.FailoverRules ?? [])
        {
            if (FailoverRuleReferences.FindUnknownProvider(config, rule) is { } unknown)
                return $"Rule '{rule.Id}' references '{unknown}', which is not a configured data source; nothing was applied.";
        }

        return null;
    }

    private static T? At<T>(T[]? items, int index) where T : class
        => items is not null && index >= 0 && index < items.Length ? items[index] : null;

    /// <summary>Path segment normalised to PascalCase so it matches the C# member names used below.</summary>
    private static string? Segment(string path, int index)
    {
        var parts = path.Split('.');
        if (index >= parts.Length || parts[index].Length == 0)
            return null;
        var part = parts[index];
        return char.ToUpperInvariant(part[0]) + part[1..];
    }

    private static ConfigurationApplyEntry Deferred(string path, string reason)
        => new(path, ConfigurationApplyKind.Deferred, reason);

    public void Dispose() => _gate.Dispose();
}
