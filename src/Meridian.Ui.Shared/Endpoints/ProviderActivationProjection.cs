using Meridian.Application.Composition.Startup;
using Meridian.Application.UI;
using Meridian.Contracts.Configuration;
using Meridian.Contracts.Lifecycle;
using Meridian.Core.Config;
using Meridian.Infrastructure.Adapters.Core;

namespace Meridian.Ui.Shared.Endpoints;

internal static class ProviderActivationProjection
{
    internal static ProviderActivationResponse Read(
        ConfigStore store,
        BackfillRoutingSnapshot? backfill,
        IReadOnlySet<string> builtProviderIds,
        RuntimeLifecycleSnapshotDto? lifecycle,
        bool supervisorConfirmed,
        bool mayRestart)
    {
        var saved = store.Load();
        var changes = store.GetChangeStatus();
        var pending = changes.PendingChanges
            .Where(change => change.Impact == ConfigurationChangeImpact.RestartHost || !changes.CanReconnectInProcess)
            .Select(change => change.Path)
            .ToArray();
        var streaming = StreamingProviderActivation.Read(store.ConfigPath)
            .ToDictionary(row => row.ProviderId, StringComparer.OrdinalIgnoreCase);
        var configuredSources = saved.DataSources?.Sources ?? [];
        for (var index = 0; index < configuredSources.Length; index++)
        {
            var source = configuredSources[index];
            var evidence = streaming.GetValueOrDefault(source.Id) ?? SavedOnly(source.Id, "streaming",
                source.Enabled, "No streaming client is running for this source in the current host mode.");
            streaming[source.Id] = ApplyState(evidence, changes, pending,
                $"DataSources.Sources.{index}", "DataSources.Sources", "DataSources.EnableFailover",
                "DataSource", source.Provider.ToString());
        }

        // A configured source can be removed while its old client remains in the collector.
        foreach (var row in streaming.Values.Where(row => configuredSources.All(source =>
                     !source.Id.Equals(row.ProviderId, StringComparison.OrdinalIgnoreCase))).ToArray())
        {
            streaming[row.ProviderId] = ApplyState(row, changes, pending, "DataSource",
                row.ProviderId, "DataSources.EnableFailover");
        }

        var historical = backfill?.Providers
            .ToDictionary(entry => ProviderIdentity.NormalizeId(entry.Provider.ProviderId), StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, BackfillRoutingProvider>(StringComparer.OrdinalIgnoreCase);
        var backfillOptions = saved.Backfill?.Providers ?? new BackfillProvidersConfig();
        var catalog = BackfillProviderCatalog.Providers.Where(provider => provider.Id != "openfigi").ToArray();
        var backfillRows = catalog.Select(provider =>
        {
            var id = ProviderIdentity.NormalizeId(provider.Id);
            var row = historical.ContainsKey(id)
                ? new ProviderActivationEntry(id, "backfill", ProviderActivationStates.Active, backfill?.AppliedAtUtc,
                    "The adapter is available to newly started backfills.")
                : SavedOnly(id, "backfill", BackfillProviderCatalog.Read(backfillOptions, provider.Id).Enabled,
                    "The running host has no eligible adapter for this provider. Review configuration and credentials.");
            return ApplyState(row, changes, pending, $"Backfill.Providers.{provider.Id}", "Backfill.Providers",
                $"ProviderQuotas.{provider.Id}", "ProviderQuotas", id);
        }).Concat(historical.Keys.Where(id => !catalog.Any(provider => ProviderIdentity.EqualsId(provider.Id, id)))
            .Select(id => new ProviderActivationEntry(id, "backfill", ProviderActivationStates.Active,
                backfill?.AppliedAtUtc, "The adapter is available to newly started backfills.")));

        var modules = saved.ProviderModules?.Modules ?? new Dictionary<string, ProviderModuleSettings>();
        var moduleRows = modules.Select(entry =>
        {
            var row = builtProviderIds.Contains(ProviderIdentity.NormalizeId(entry.Key))
                ? new ProviderActivationEntry(entry.Key, "module", ProviderActivationStates.Active,
                    lifecycle?.StartedAtUtc, "The host registered a provider adapter at startup.")
                : SavedOnly(entry.Key, "module", entry.Value.Enabled,
                    "The host has no running adapter for this module. Review its configuration and credentials.");
            return ApplyState(row, changes, pending, $"ProviderModules.Modules.{entry.Key}", "ProviderModules.Modules");
        });

        var isReady = IsReady(lifecycle);
        var canRestart = mayRestart && supervisorConfirmed && isReady;
        var detail = canRestart ? null : !supervisorConfirmed
            ? "Restart Meridian using its launcher or the process manager that started it."
            : !mayRestart
                ? "A host administrator can restart Meridian to apply these settings."
                : "Host restart is available when Meridian has finished starting.";

        return new ProviderActivationResponse(streaming.Values.Concat(backfillRows).Concat(moduleRows)
            .OrderBy(row => row.Surface, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.ProviderId, StringComparer.OrdinalIgnoreCase)
            .ToArray(), canRestart, pending, detail, lifecycle?.SessionId, isReady);
    }

    internal static bool IsReady(RuntimeLifecycleSnapshotDto? lifecycle)
        => lifecycle is { AcceptingWork: true, Readiness: RuntimeReadinessStatus.Ready, State: RuntimeLifecycleState.Ready };

    private static ProviderActivationEntry SavedOnly(string id, string surface, bool enabled, string reason)
        => enabled
            ? new(id, surface, ProviderActivationStates.Unavailable, null, reason)
            : new(id, surface, ProviderActivationStates.Disabled, null, "The provider is disabled in the saved configuration.");

    private static ProviderActivationEntry ApplyState(
        ProviderActivationEntry row, ConfigurationChangeStatus changes, IReadOnlyList<string> pending, params string[] scopes)
    {
        var apply = changes.LastApply?.Entries.LastOrDefault(entry =>
            scopes.Any(scope => PathMatches(entry.Path, scope)));
        if (pending.Any(path => scopes.Any(scope => PathMatches(path, scope))))
        {
            return row with
            {
                State = ProviderActivationStates.SavedPending,
                Reason = apply is { Kind: ConfigurationApplyKind.Deferred, Detail: not null }
                    ? apply.Detail
                    : "Saved settings take effect after the host restarts."
            };
        }

        return apply is { Kind: ConfigurationApplyKind.NoRuntimeEffect, Detail: not null }
            ? row with { Reason = apply.Detail }
            : row;
    }

    private static bool PathMatches(string path, string scope)
        => path.Equals(scope, StringComparison.OrdinalIgnoreCase)
            || (scope is not ("DataSources.Sources" or "Backfill.Providers" or "ProviderModules.Modules" or "ProviderQuotas") &&
                path.StartsWith(scope + ".", StringComparison.OrdinalIgnoreCase));
}
