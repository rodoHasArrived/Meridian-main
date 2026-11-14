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

/// <summary>Shared by browser and desktop clients. Pending means saved, but not confirmed applied.</summary>
public sealed record ConfigurationChangeStatus(
    ConfigurationChangeImpact Impact,
    IReadOnlyList<ConfigurationFieldChange> Changes,
    IReadOnlyList<ConfigurationFieldChange> PendingChanges,
    DateTimeOffset? SavedAtUtc,
    bool CanReconnectInProcess = false)
{
    public bool RequiresRestart => PendingChanges.Any(change => change.Impact == ConfigurationChangeImpact.RestartHost
        || !CanReconnectInProcess && change.Impact == ConfigurationChangeImpact.ReconnectProvider);
    public bool RequiresReconnect => CanReconnectInProcess
        && PendingChanges.Any(change => change.Impact == ConfigurationChangeImpact.ReconnectProvider);
}
