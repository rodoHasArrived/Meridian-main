using System.Text.Json.Serialization;

namespace Meridian.Contracts.Configuration;

/// <summary>How a saved setting reaches the running host.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ConfigurationChangeImpact>))]
public enum ConfigurationChangeImpact
{
    Live,
    ReconnectProvider,
    RestartHost
}

/// <summary>Contains setting names only; configuration and credential values are never exported.</summary>
public sealed record ConfigurationFieldChange(string Path, ConfigurationChangeImpact Impact);

/// <summary>What the running host did with one saved setting path.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ConfigurationApplyKind>))]
public enum ConfigurationApplyKind
{
    /// <summary>The running host now uses the saved value.</summary>
    AppliedLive,

    /// <summary>A streaming provider was excluded from or re-included in failover in-process.</summary>
    ProviderReconfigured,

    /// <summary>The value was saved; it has no runtime effect in the current host mode.</summary>
    NoRuntimeEffect,

    /// <summary>The value was saved but the running host could not apply it; a restart is required.</summary>
    Deferred
}

/// <summary>
/// One runtime-apply outcome. <see cref="Detail"/> is a fixed explanation and may name provider or rule
/// IDs; it never contains configuration or credential values.
/// </summary>
public sealed record ConfigurationApplyEntry(string Path, ConfigurationApplyKind Kind, string? Detail = null);

/// <summary>Outcome of the most recent save's runtime apply, reported by the host component that applied it.</summary>
public sealed record ConfigurationApplySummary(
    IReadOnlyList<ConfigurationApplyEntry> Entries,
    DateTimeOffset AppliedAtUtc)
{
    public bool AnyDeferred => Entries.Any(entry => entry.Kind == ConfigurationApplyKind.Deferred);
}

/// <summary>Shared by browser and desktop clients. Pending means saved, but not confirmed applied.</summary>
public sealed record ConfigurationChangeStatus(
    ConfigurationChangeImpact Impact,
    IReadOnlyList<ConfigurationFieldChange> Changes,
    IReadOnlyList<ConfigurationFieldChange> PendingChanges,
    DateTimeOffset? SavedAtUtc,
    bool CanReconnectInProcess = false,
    ConfigurationApplySummary? LastApply = null)
{
    public bool RequiresRestart => PendingChanges.Any(change => change.Impact == ConfigurationChangeImpact.RestartHost
        || !CanReconnectInProcess && change.Impact == ConfigurationChangeImpact.ReconnectProvider);
    public bool RequiresReconnect => CanReconnectInProcess
        && PendingChanges.Any(change => change.Impact == ConfigurationChangeImpact.ReconnectProvider);
}
